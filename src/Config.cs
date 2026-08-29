using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace Vanta
{
    public enum CadenceMode { Rate, Delay }
    public enum TimeUnit { Milliseconds, Seconds, Minutes, Hours }
    public enum ClickButton { Left, Middle, Right }
    public enum ActivationMode { Toggle, Hold }
    public enum LimitMode { Clicks, Seconds }

    [Flags]
    public enum HotkeyModifiers { None = 0, Control = 1, Alt = 2, Shift = 4, Windows = 8 }

    public sealed class SequencePoint
    {
        public int X { get; set; }
        public int Y { get; set; }
        public SequencePoint() { }
        public SequencePoint(int x, int y) { X = x; Y = y; }
        public override string ToString() { return String.Format(CultureInfo.InvariantCulture, "X  {0}      Y  {1}", X, Y); }
    }

    public sealed class ClickSettings
    {
        public CadenceMode Cadence { get; set; }
        public double Amount { get; set; }
        public TimeUnit Unit { get; set; }
        public ClickButton Button { get; set; }
        public ActivationMode Activation { get; set; }
        public int HotkeyKey { get; set; }
        public HotkeyModifiers HotkeyMods { get; set; }
        public double DurationPercent { get; set; }
        public bool VariationEnabled { get; set; }
        public double VariationPercent { get; set; }
        public bool LimitEnabled { get; set; }
        public LimitMode Limit { get; set; }
        public double LimitValue { get; set; }
        public bool DoubleClickEnabled { get; set; }
        public double DoubleClickGapMs { get; set; }
        public bool SequenceEnabled { get; set; }
        public List<SequencePoint> Points { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool MinimizeOnStart { get; set; }
        public string View { get; set; }

        public ClickSettings()
        {
            Cadence = CadenceMode.Rate;
            Amount = 10;
            Unit = TimeUnit.Seconds;
            Button = ClickButton.Left;
            Activation = ActivationMode.Toggle;
            HotkeyKey = 0x77; // F8
            DurationPercent = 50;
            VariationPercent = 15;
            LimitValue = 100;
            DoubleClickGapMs = 20;
            Points = new List<SequencePoint>();
            View = "Default";
        }

        public double IntervalMs
        {
            get
            {
                double unit = Unit == TimeUnit.Milliseconds ? 1 : Unit == TimeUnit.Seconds ? 1000 : Unit == TimeUnit.Minutes ? 60000 : 3600000;
                return Cadence == CadenceMode.Rate ? unit / Amount : unit * Amount;
            }
        }

        public string Validate(bool forRun)
        {
            if (!Enum.IsDefined(typeof(CadenceMode), Cadence) || !Enum.IsDefined(typeof(TimeUnit), Unit) ||
                !Enum.IsDefined(typeof(ClickButton), Button) || !Enum.IsDefined(typeof(ActivationMode), Activation) ||
                !Enum.IsDefined(typeof(LimitMode), Limit)) return "A saved option is not recognized. Reset your settings.";
            if (!Finite(Amount) || Amount <= 0) return "Enter a rate or delay greater than zero.";
            if (!Finite(IntervalMs) || IntervalMs < 1 || IntervalMs > 86400000) return "Choose a cadence between 1 millisecond and 24 hours (up to 1,000 cycles per second).";
            if (!Finite(DurationPercent) || DurationPercent < 0 || DurationPercent > 100) return "Click duration must be between 0 and 100%.";
            if (!Finite(VariationPercent) || VariationPercent < 0 || VariationPercent > 90) return "Speed variation must be between 0 and 90%.";
            if (!Finite(DoubleClickGapMs) || DoubleClickGapMs < 1 || DoubleClickGapMs > 5000) return "The double click gap must be between 1 and 5,000 milliseconds.";
            if (!Finite(LimitValue) || LimitValue < 1 || LimitValue > 1000000000) return "The limit must be between 1 and 1,000,000,000.";
            if (LimitEnabled && Limit == LimitMode.Clicks && LimitValue != Math.Floor(LimitValue)) return "The click limit must be a whole number.";
            if (!ValidHotkey(HotkeyKey, HotkeyMods)) return "Choose a letter, number, function key, or navigation key. Esc and F6 are reserved.";
            if (Points == null || Points.Count > 1000 || Points.Any(p => p == null || Math.Abs((long)p.X) > 100000 || Math.Abs((long)p.Y) > 100000)) return "Sequence points are invalid (maximum 1,000 points).";
            if (forRun && SequenceEnabled && Points.Count == 0) return "Add at least one cursor point, or turn sequence clicking off.";
            return null;
        }

        public static bool ValidHotkey(int key, HotkeyModifiers mods)
        {
            bool allowed = (key >= 0x30 && key <= 0x5A) || (key >= 0x70 && key <= 0x87) ||
                (key >= 0x60 && key <= 0x6F) || (key >= 0x21 && key <= 0x28) || key == 0x2D || key == 0x2E || key == 0x20;
            return allowed && key != 0x75 && (((int)mods & ~15) == 0);
        }

        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }

        public ClickSettings Copy()
        {
            var copy = (ClickSettings)MemberwiseClone();
            copy.Points = Points.Select(p => new SequencePoint(p.X, p.Y)).ToList();
            return copy;
        }
    }

    public static class SettingsStore
    {
        public static string DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vanta Auto Clicker", "settings.xml"); }
        }

        public static ClickSettings Load(string path, out string warning)
        {
            warning = null;
            if (!File.Exists(path)) return new ClickSettings();
            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 }))
                {
                    var settings = (ClickSettings)new XmlSerializer(typeof(ClickSettings)).Deserialize(reader);
                    string error = settings.Validate(false);
                    if (error != null) throw new InvalidDataException(error);
                    return settings;
                }
            }
            catch (Exception ex)
            {
                warning = "Saved settings could not be read. Defaults are loaded. " + ex.GetBaseException().Message;
                return new ClickSettings();
            }
        }

        public static void Save(string path, ClickSettings settings)
        {
            string error = settings.Validate(false);
            if (error != null) throw new InvalidDataException(error);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    new XmlSerializer(typeof(ClickSettings)).Serialize(stream, settings);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
