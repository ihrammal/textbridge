"""Publish verified TextBridge artifacts through GitHub's release API.

Uses the existing Git credential helper in memory. Never logs or stores tokens.
Draft creation/upload and public publication are separate explicit operations.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import urllib.error
import urllib.parse
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
REPO='ihrammal/textbridge'
API='https://api.github.com/repos/'+REPO
parser=argparse.ArgumentParser()
parser.add_argument('action',choices=['status','draft','publish','verify'])
parser.add_argument('--commit',required=True)
parser.add_argument('--checkout',default=str(ROOT/'.tools/textbridge-github'))
parser.add_argument('--version',default='1.1.0',help='Release/update build identifier; display version is unchanged.')
args=parser.parse_args()
VERSION=args.version
TAG='v'+VERSION
ARTIFACTS=ROOT/'artifacts'

def token():
    environment=dict(os.environ,GIT_TERMINAL_PROMPT='0',GCM_INTERACTIVE='Never')
    result=subprocess.run(['git','-C',args.checkout,'credential','fill'],input='protocol=https\nhost=github.com\n\n',capture_output=True,text=True,env=environment)
    if result.returncode:
        raise RuntimeError('The existing Git credential helper could not authorize the GitHub release API.')
    fields=dict(line.split('=',1) for line in result.stdout.splitlines() if '=' in line)
    if not fields.get('password'):raise RuntimeError('No GitHub API credential was returned.')
    return fields['password']

credential=token()
def api(path='',method='GET',data=None,raw=None,content_type='application/json'):
    url=path if path.startswith('https://') else API+path
    if urllib.parse.urlparse(url).hostname not in ('api.github.com','uploads.github.com'):
        raise ValueError('Unexpected GitHub API host')
    request=urllib.request.Request(url,data=raw if raw is not None else json.dumps(data).encode() if data is not None else None,method=method,
        headers={'Authorization':'Bearer '+credential,'Accept':'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28','User-Agent':'TextBridge-release','Content-Type':content_type})
    try:
        with urllib.request.urlopen(request,timeout=180) as response:
            body=response.read()
        return json.loads(body) if body else None
    except urllib.error.HTTPError as error:
        raise RuntimeError(f'GitHub API returned HTTP {error.code} for {method} {urllib.parse.urlparse(url).path}') from None

def checks():
    runs=api('/actions/runs?'+urllib.parse.urlencode(dict(head_sha=args.commit,per_page=30)))['workflow_runs']
    runs=[run for run in runs if run['name']=='Windows build validation']
    if not runs:return False,[]
    # A completed successful run on the exact commit is required. Do not infer
    # success from a different revision or from a missing/pending check.
    return any(run['conclusion']=='success' for run in runs),[dict(id=run['id'],status=run['status'],conclusion=run['conclusion'],url=run['html_url']) for run in runs]

passed,runs=checks()
if args.action=='status':
    print(json.dumps(dict(commit=args.commit,passed=passed,runs=runs),indent=2))
    raise SystemExit()

manifest=json.loads((ARTIFACTS/f'release-manifest-{VERSION}.json').read_text(encoding='utf-8-sig'))
releases=api('/releases?per_page=50')
release=next((item for item in releases if item['tag_name']==TAG),None)
if args.action in ('draft','publish') and not passed:
    raise RuntimeError('Fresh GitHub Windows validation has not passed on the exact source commit.')
assets=list(manifest['assets'])
for filename in [f'release-manifest-{VERSION}.json','SHA256SUMS.txt']:
    file=ARTIFACTS/filename
    assets.append(dict(file=filename,bytes=file.stat().st_size,sha256=hashlib.sha256(file.read_bytes()).hexdigest()))

if args.action=='draft':
    if release and not release['draft']:raise RuntimeError('This release is already public; published assets will not be replaced.')
    notes=(ROOT/f'docs/RELEASE-NOTES-{VERSION}.md').read_text(encoding='utf-8')
    notes+='\n\nVerified source: `'+args.commit+'`.\n\n'+('\n'.join('[Windows validation]('+run['url']+')' for run in runs if run['conclusion']=='success'))
    if release is None:
        release=api('/releases','POST',dict(tag_name=TAG,target_commitish=args.commit,name='TextBridge 1.1.0 — corrected build 1' if VERSION=='1.1.0.1' else 'TextBridge '+VERSION,body=notes,draft=True,prerelease=False))
    else:
        if release['target_commitish']!=args.commit:raise RuntimeError('Existing draft targets a different source commit.')
        release=api('/releases/'+str(release['id']),'PATCH',dict(body=notes))
    existing={item['name']:item for item in api('/releases/'+str(release['id'])+'/assets?per_page=100')}
    for asset in assets:
        path=ARTIFACTS/asset['file']
        digest=hashlib.sha256(path.read_bytes()).hexdigest()
        if digest!=asset['sha256'] or path.stat().st_size!=asset['bytes']:
            raise RuntimeError('Local asset differs from verified manifest: '+path.name)
        if path.name in existing:
            if existing[path.name].get('digest')!='sha256:'+digest:
                raise RuntimeError('An existing draft asset differs: '+path.name)
            continue
        url=release['upload_url'].split('{')[0]+'?'+urllib.parse.urlencode(dict(name=path.name))
        result=api(url,'POST',raw=path.read_bytes(),content_type='application/octet-stream')
        if result.get('digest')!='sha256:'+digest:raise RuntimeError('Uploaded asset digest mismatch: '+path.name)
        print('Uploaded and verified '+path.name,flush=True)
    print(json.dumps(dict(id=release['id'],url=release['html_url'],draft=True)))
elif args.action=='publish':
    if release is None or not release['draft']:raise RuntimeError('Expected a verified draft release.')
    if not manifest.get('readyForPublicRelease'):raise RuntimeError('The local release manifest is not marked ready.')
    if release['target_commitish']!=args.commit:raise RuntimeError('Draft source revision changed.')
    uploaded={item['name']:item for item in api('/releases/'+str(release['id'])+'/assets?per_page=100')}
    for asset in assets:
        remote=uploaded.get(asset['file'])
        if remote is None or remote.get('digest')!='sha256:'+asset['sha256']:
            raise RuntimeError('Draft asset verification failed: '+asset['file'])
    release=api('/releases/'+str(release['id']),'PATCH',dict(draft=False,make_latest='true'))
    print(json.dumps(dict(id=release['id'],url=release['html_url'],published=not release['draft'])))
elif args.action=='verify':
    if release is None or release['draft']:raise RuntimeError('The public release does not exist.')
    request=urllib.request.Request(API+'/releases/latest',headers={'User-Agent':'TextBridge-release-verification'})
    with urllib.request.urlopen(request,timeout=60) as response:latest=json.load(response)
    if latest['tag_name']!=TAG:raise RuntimeError('The anonymous latest-release feed does not return the new version.')
    installer=next(asset for asset in assets if asset['file']==f'TextBridge-Setup-{VERSION}.exe')
    url=next(item['browser_download_url'] for item in latest['assets'] if item['name']==installer['file'])
    digest=hashlib.sha256();size=0
    with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'TextBridge-release-verification'}),timeout=180) as response:
        while chunk:=response.read(1024*1024):digest.update(chunk);size+=len(chunk)
    if digest.hexdigest()!=installer['sha256'] or size!=installer['bytes']:
        raise RuntimeError('Anonymous installer download differs from the verified release.')
    result=dict(version=VERSION,commit=args.commit,releaseUrl=latest['html_url'],installerUrl=url,sha256=digest.hexdigest(),bytes=size,anonymousLatestFeedVerified=True,anonymousInstallerDownloadVerified=True,runs=runs)
    (ARTIFACTS/f'textbridge-publication-{VERSION}.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps(result,indent=2))
