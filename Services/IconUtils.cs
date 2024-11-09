using Newtonsoft.Json;
using Owcounter.Model;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace Owcounter.Services
{
    public class HeroPortraitData
    {
        public required string Key { get; set; }
        public required string Name { get; set; }
        public required string Portrait { get; set; }
        public required string Role { get; set; }
    }

    public static class IconUtils
    {
        private static readonly Dictionary<HeroName, Image> Icons = new();
        private static readonly HttpClient httpClient = new();

        private static string ConvertEnumToApiKey(HeroName heroName)
        {
            return heroName switch
            {
                HeroName.Soldier76 => "soldier-76",
                HeroName.JunkerQueen => "junker-queen",
                HeroName.WreckingBall => "wrecking-ball",
                HeroName.Dva => "dva",
                _ => heroName.ToString().ToLower().Replace("_", "-")
            };
        }

        public static void Initialize()
        {
            try
            {
                var response = httpClient.GetStringAsync("https://overfast-api.tekrop.fr/heroes").GetAwaiter().GetResult();
                var heroes = JsonConvert.DeserializeObject<List<HeroPortraitData>>(response)
                    ?? throw new JsonException("Failed to deserialize hero data");

                foreach (HeroName heroName in Enum.GetValues(typeof(HeroName)))
                {
                    if (heroName is HeroName.Unknown or HeroName.Hidden or HeroName.NameUnspecified)
                        continue;

                    var apiKey = ConvertEnumToApiKey(heroName);
                    if (heroes.FirstOrDefault(h => h.Key == apiKey)?.Portrait is string portraitUrl)
                    {
                        DownloadIcon(heroName, portraitUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to initialize hero portraits: {ex.Message}");
                throw;
            }
        }

        private static void DownloadIcon(HeroName heroName, string url)
        {
            var data = httpClient.GetByteArrayAsync(url).GetAwaiter().GetResult();
            using var ms = new MemoryStream(data);
            using var originalImage = Image.FromStream(ms);
            Icons[heroName] = new Bitmap(originalImage);
        }

        public static Image GetHeroIconSync(HeroName heroName, int size = 64)
        {
            if (heroName is HeroName.Unknown or HeroName.Hidden or HeroName.NameUnspecified)
            {
                return CreatePlaceholderIcon("???", size);
            }

            if (Icons.TryGetValue(heroName, out var icon))
            {
                return ResizeImage(icon, size, size);
            }

            return CreatePlaceholderIcon(heroName.ToString(), size);
        }

        public static BitmapSource GetHeroIconAsBitmapSource(HeroName heroName, int size = 64)
        {
            using var image = GetHeroIconSync(heroName, size);
            return CreateBitmapSourceFromImage(image);
        }

        private static Image ResizeImage(Image image, int width, int height)
        {
            var destRect = new Rectangle(0, 0, width, height);
            var destImage = new Bitmap(width, height);

            destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var wrapMode = new ImageAttributes())
                {
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
                }
            }

            return destImage;
        }

        private static BitmapSource CreateBitmapSourceFromImage(Image image)
        {
            using var ms = new MemoryStream();
            image.Save(ms, ImageFormat.Png);
            ms.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = ms;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }

        private static Image CreatePlaceholderIcon(string heroName, int size)
        {
            var bitmap = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(40, 40, 40));
                float fontSize = size / 4f;
                g.DrawString(heroName.Substring(0, Math.Min(3, heroName.Length)).ToUpper(),
                    new Font("Segoe UI", fontSize, FontStyle.Bold),
                    Brushes.White,
                    new RectangleF(0, 0, size, size),
                    new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    });
            }
            return bitmap;
        }
    }
}