"""TextBridge's offline, JSON-lines PaddleOCR worker (stdin/stdout only)."""
import contextlib
import json
import os
from pathlib import Path
import socket
import sys

sys.stdin.reconfigure(encoding='utf-8')
sys.stdout.reconfigure(encoding='utf-8')
sys.stderr.reconfigure(encoding='utf-8')

ROOT = Path(__file__).resolve().parent
MODELS = ROOT / 'models'
CACHE = Path(os.environ.get('LOCALAPPDATA', str(ROOT))) / 'TextBridge' / 'OcrCache'
CACHE.mkdir(parents=True, exist_ok=True)
# All caches are writable per-user; model files stay in the application directory.
os.environ['USERPROFILE'] = str(CACHE)
os.environ['PADDLE_PDX_CACHE_HOME'] = str(CACHE / 'paddlex')
os.environ['PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK'] = 'True'
os.environ['HF_HUB_OFFLINE'] = '1'
os.environ['HF_HOME'] = str(CACHE / 'huggingface')
os.environ['MODELSCOPE_CACHE'] = str(CACHE / 'modelscope')
os.environ['OMP_NUM_THREADS'] = '4'

def no_network(*args, **kwargs):
    raise RuntimeError('TextBridge local OCR does not allow network connections.')

socket.create_connection = no_network
socket.socket.connect = no_network
socket.socket.connect_ex = no_network

# Third-party startup/progress messages belong on stderr, never in the protocol.
with contextlib.redirect_stdout(sys.stderr):
    from paddleocr import PaddleOCR

ENGINES = {}
MODELNAMES = {'ara': 'arabic_PP-OCRv5_mobile_rec', 'eng': 'en_PP-OCRv5_mobile_rec', 'fra': 'latin_PP-OCRv5_mobile_rec'}

def model_path(name):
    path = MODELS / name
    if not (path / 'inference.json').is_file() or not (path / 'inference.pdiparams').is_file():
        raise RuntimeError(f'The offline OCR model is missing: {name}. Reinstall TextBridge.')
    return str(path)

def engine(language, rotate):
    key = (language, rotate)
    if key not in ENGINES:
        kwargs = {}
        if rotate:
            kwargs = dict(doc_orientation_classify_model_name='PP-LCNet_x1_0_doc_ori', doc_orientation_classify_model_dir=model_path('PP-LCNet_x1_0_doc_ori'))
        ENGINES[key] = PaddleOCR(
            text_detection_model_name='PP-OCRv5_mobile_det', text_detection_model_dir=model_path('PP-OCRv5_mobile_det'),
            text_recognition_model_name=MODELNAMES[language], text_recognition_model_dir=model_path(MODELNAMES[language]),
            use_doc_orientation_classify=rotate, use_doc_unwarping=False, use_textline_orientation=False,
            # The bundled v5 mobile detector is configured for a 960-pixel long
            # edge. Explicitly cap detection instead of inheriting the generic
            # pipeline's minimum-side setting, which kept full 300-dpi pages.
            # Recognition still uses crops from the original-resolution image.
            text_det_limit_side_len=960, text_det_limit_type='max',
            return_word_box=True, device='cpu', cpu_threads=4, enable_mkldnn=False, **kwargs)
    return ENGINES[key]

def overlap(a, b):
    inter = max(0,min(a[2],b[2])-max(a[0],b[0])) * max(0,min(a[3],b[3])-max(a[1],b[1]))
    return inter / max(1,min((a[2]-a[0])*(a[3]-a[1]),(b[2]-b[0])*(b[3]-b[1])))

def recognize(request):
    languages = request.get('languages','ara+eng+fra').split('+')
    if not languages or any(language not in MODELNAMES for language in languages):
        raise ValueError('Unsupported OCR language: ' + '+'.join(languages) + '. Choose Arabic, English, French, or the combined profile.')
    # The Latin recognizer handles French and English together; use the dedicated
    # English recognizer for an English-only document.
    selected = (['ara'] if 'ara' in languages else []) + (['fra'] if 'fra' in languages else ['eng'] if 'eng' in languages else [])
    candidates = []
    width = height = 0
    for language in selected:
        for prediction in engine(language, bool(request.get('rotate',False))).predict(request['image']):
            from PIL import Image
            with Image.open(request['image']) as image:
                width,height = image.size
            # Coordinates refer to the preprocessed upright image. Consumers use
            # this same image for searchable PDF appearance and text placement.
            orientation = prediction.get('doc_preprocessor_res',{}).get('angle',-1)
            if orientation in (90,270):
                width,height = height,width
            for index,text in enumerate(prediction['rec_texts']):
                words = prediction.get('text_word',[])
                boxes = prediction.get('text_word_boxes',[])
                word_texts = words[index] if len(words)>index else [text]
                word_boxes = boxes[index] if len(boxes)>index else [prediction['rec_boxes'][index]]
                # Upstream Arabic word alignment returns visual-order text. Keep
                # the correctly decoded logical line as one review/geometry span.
                if any('\u0600' <= character <= '\u06ff' for character in text):
                    word_texts = [text]
                    word_boxes = [prediction['rec_boxes'][index]]
                candidates.append(dict(text=text, confidence=float(prediction['rec_scores'][index])*100,
                    box=[int(v) for v in prediction['rec_boxes'][index]], language=language, angle=orientation,
                    words=[dict(text=str(t),box=[int(v) for v in b]) for t,b in zip(word_texts,word_boxes)]))
    groups = []
    for line in candidates:
        group = next((g for g in groups if overlap(g[0]['box'],line['box'])>.7),None)
        if group is None:
            groups.append([line])
        else:
            group.append(line)
    lines = [max(group,key=lambda line:line['confidence']) for group in groups]
    # Paddle's detector already orders lines. Stable geometry ordering provides
    # consistent interleaving when multiple script recognizers contribute lines.
    lines.sort(key=lambda line:(line['box'][1],line['box'][0]))
    if request.get('preserveLayout',False):
        for line in lines:
            words = line['words']
            if len(words)>1:
                average = sum(max(1,word['box'][2]-word['box'][0]) for word in words)/max(1,sum(len(word['text']) for word in words))
                text = words[0]['text']
                for previous,word in zip(words,words[1:]):
                    gap = max(1,min(20,round((word['box'][0]-previous['box'][2])/max(1,average))))
                    text += ' '*gap + word['text']
                line['text'] = text
    rows = []
    for line in lines:
        center = (line['box'][1]+line['box'][3])/2
        matching = next((row for row in rows if abs(center-row[0]) < max(3,min(line['box'][3]-line['box'][1],row[1])*.45)),None)
        if matching is None:
            rows.append([center,line['box'][3]-line['box'][1],[line]])
        else:
            matching[2].append(line)
    rows.sort(key=lambda row:row[0])
    ordered = []
    text_rows = []
    for _,_,row in rows:
        arabic_count = sum(sum('\u0600' <= c <= '\u06ff' for c in line['text']) for line in row)
        latin_count = sum(sum(c.isalpha() and ord(c)<600 for c in line['text']) for line in row)
        rtl = arabic_count>latin_count
        row.sort(key=lambda line:line['box'][0],reverse=rtl)
        text = row[0]['text']
        for previous,line in zip(row,row[1:]):
            spaces = 1
            if request.get('preserveLayout',False):
                average = (previous['box'][2]-previous['box'][0])/max(1,len(previous['text']))
                gap = previous['box'][0]-line['box'][2] if rtl else line['box'][0]-previous['box'][2]
                spaces = max(1,min(40,round(gap/max(1,average))))
            text += ' '*spaces + line['text']
        text_rows.append(text)
        ordered.extend(row)
    return dict(text='\n'.join(text_rows), lines=ordered, width=width,height=height,
        angle=lines[0]['angle'] if lines else 0, engine='PaddleOCR PP-OCRv5')

for raw in sys.stdin:
    try:
        request = json.loads(raw)
        with contextlib.redirect_stdout(sys.stderr):
            result = recognize(request)
        print(json.dumps(dict(ok=True,result=result),ensure_ascii=False),flush=True)
    except Exception as exc:
        print(json.dumps(dict(ok=False,error=str(exc)),ensure_ascii=False),flush=True)
