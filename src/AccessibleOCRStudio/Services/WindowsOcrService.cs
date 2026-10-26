using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace AccessibleOCRStudio.Services;

public sealed class WindowsOcrService
{
    private static readonly Dictionary<string,string> Codes=new() { ["ara"]="ar",["eng"]="en",["fra"]="fr" };
    public static IReadOnlyList<string> AvailableProfiles
    {
        get
        {
            try { return Codes.Where(pair=>OcrEngine.AvailableRecognizerLanguages.Any(language=>language.LanguageTag.Split('-')[0]==pair.Value)).Select(pair=>pair.Key).ToArray(); }
            catch { return Array.Empty<string>(); }
        }
    }

    public Task<OcrPageResult> RecognizeAsync(string image,AppSettings settings,CancellationToken cancellationToken = default)
        => Task.Run(()=>RecognizeCoreAsync(image,settings,cancellationToken));

    private static async Task<OcrPageResult> RecognizeCoreAsync(string image,AppSettings settings,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profiles=settings.OcrLanguages.Split('+',StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        if(profiles.Length==0 || profiles.Any(profile=>!Codes.ContainsKey(profile)))
            throw new InvalidOperationException("Unsupported Windows OCR language: "+settings.OcrLanguages);
        var languages=new List<Language>();
        foreach(var profile in profiles)
        {
            var language=OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(language=>language.LanguageTag.Split('-')[0]==Codes[profile]);
            if(language is null) throw new InvalidOperationException($"Windows OCR language pack is missing: {Codes[profile]}. Install the language's OCR feature in Windows Settings, choose another document language, or use PaddleOCR.");
            languages.Add(language);
        }
        var file=await StorageFile.GetFileFromPathAsync(Path.GetFullPath(image));
        using var stream=await file.OpenAsync(FileAccessMode.Read);
        var decoder=await BitmapDecoder.CreateAsync(stream);
        var ratio=Math.Min(1d,(double)OcrEngine.MaxImageDimension/Math.Max(decoder.PixelWidth,decoder.PixelHeight));
        var transform=new BitmapTransform { ScaledWidth=Math.Max(1,(uint)Math.Round(decoder.PixelWidth*ratio)),ScaledHeight=Math.Max(1,(uint)Math.Round(decoder.PixelHeight*ratio)) };
        using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,transform,ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage);
        var groups=new List<List<(OcrLineResult Line,string Language)>>();
        foreach(var language in languages)
        {
            var engine=OcrEngine.TryCreateFromLanguage(language) ?? throw new InvalidOperationException("Windows OCR is unavailable for "+language.LanguageTag);
            var result=await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
            foreach(var source in result.Lines)
            {
                var words=source.Words.Select(word=>new OcrSpanResult(word.Text,new[] {
                    (int)Math.Round(word.BoundingRect.X/ratio),(int)Math.Round(word.BoundingRect.Y/ratio),
                    (int)Math.Round((word.BoundingRect.X+word.BoundingRect.Width)/ratio),(int)Math.Round((word.BoundingRect.Y+word.BoundingRect.Height)/ratio) })).ToList();
                if(words.Count==0) continue;
                var rtl=words.Sum(word=>word.Text.Count(ArabicLetter))>words.Sum(word=>word.Text.Count(c=>char.IsLetter(c)&&!ArabicLetter(c)));
                words=words.OrderBy(word=>rtl?-word.Box[0]:word.Box[0]).ToList();
                // Keep opposite-direction runs in their own logical order.
                for(var start=0;start<words.Count;)
                {
                    var opposite=rtl ? !words[start].Text.Any(ArabicLetter) : words[start].Text.Any(ArabicLetter);
                    if(!opposite){start++;continue;}
                    var end=start+1;
                    while(end<words.Count && (rtl ? !words[end].Text.Any(ArabicLetter) : words[end].Text.Any(ArabicLetter)))end++;
                    words.Reverse(start,end-start);start=end;
                }
                var text=words[0].Text;
                for(var i=1;i<words.Count;i++)
                {
                    var spaces=1;
                    if(settings.PreserveTextLayout)
                    {
                        var average=(words[i-1].Box[2]-words[i-1].Box[0])/(double)Math.Max(1,words[i-1].Text.Length);
                        var gap=rtl?words[i-1].Box[0]-words[i].Box[2]:words[i].Box[0]-words[i-1].Box[2];
                        spaces=Math.Clamp((int)Math.Round(gap/Math.Max(1,average)),1,40);
                    }
                    text+=new string(' ',spaces)+words[i].Text;
                }
                var box=new[]{words.Min(w=>w.Box[0]),words.Min(w=>w.Box[1]),words.Max(w=>w.Box[2]),words.Max(w=>w.Box[3])};
                // Windows.Media.Ocr exposes no confidence score. -1 means
                // unavailable, rather than fabricating a recognition estimate.
                var line=new OcrLineResult(text,-1,box,words.ToArray());
                var group=groups.FirstOrDefault(group=>Overlap(group[0].Line.Box,box)>.7);
                if(group is null)groups.Add(new(){(line,language.LanguageTag)});else group.Add((line,language.LanguageTag));
            }
        }
        var lines=groups.Select(group=>
        {
            var arabic=group.FirstOrDefault(candidate=>candidate.Language.StartsWith("ar") && candidate.Line.Text.Count(ArabicLetter)>2);
            if(arabic.Line is not null)return arabic.Line;
            return group.OrderByDescending(candidate=>candidate.Line.Text.Count(c=>char.IsLetter(c)&&c>127)).ThenByDescending(candidate=>candidate.Language.StartsWith("fr")).First().Line;
        }).OrderBy(line=>line.Box[1]).ThenBy(line=>line.Box[0]).ToArray();
        return new(string.Join(Environment.NewLine,lines.Select(line=>line.Text)),lines,(int)decoder.PixelWidth,(int)decoder.PixelHeight,0);
    }

    private static bool ArabicLetter(char c)=>c is >= '\u0600' and <= '\u06ff' && char.IsLetter(c);
    private static double Overlap(int[] a,int[] b)
    {
        var intersection=(double)Math.Max(0,Math.Min(a[2],b[2])-Math.Max(a[0],b[0]))*Math.Max(0,Math.Min(a[3],b[3])-Math.Max(a[1],b[1]));
        return intersection/Math.Max(1,Math.Min((double)(a[2]-a[0])*(a[3]-a[1]),(double)(b[2]-b[0])*(b[3]-b[1])));
    }
}
