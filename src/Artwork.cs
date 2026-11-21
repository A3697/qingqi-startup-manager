using System.Drawing;
using System.IO;

namespace QingQi
{
    public static class Artwork
    {
        public static readonly Icon Main = Load("QingQi.Main.ico");
        public static readonly Icon Worker = Load("QingQi.Worker.ico");
        private static Icon Load(string name)
        {
            using (Stream stream = typeof(Artwork).Assembly.GetManifestResourceStream(name))
            using (var icon = new Icon(stream, 32, 32)) return (Icon)icon.Clone();
        }
        public static Bitmap ForFile(string path)
        {
            try { using (Icon icon = Icon.ExtractAssociatedIcon(path)) if (icon != null) return icon.ToBitmap(); }
            catch { }
            return Main.ToBitmap();
        }
    }
}
