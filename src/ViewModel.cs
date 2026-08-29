using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;

namespace Vanta
{
    public sealed class ViewModel : INotifyPropertyChanged
    {
        private ClickSettings settings;
        private string amount, duration, variation, limit, gap;
        private bool canEdit = true;
        public ObservableCollection<SequencePoint> Points { get; private set; }
        public event PropertyChangedEventHandler PropertyChanged;
        public event Action Changed;
        public ViewModel(ClickSettings settings) { Load(settings); }
        public void Load(ClickSettings value)
        {
            settings = value.Copy();
            amount = Format(value.Amount); duration = Format(value.DurationPercent); variation = Format(value.VariationPercent);
            limit = Format(value.LimitValue); gap = Format(value.DoubleClickGapMs);
            Points = new ObservableCollection<SequencePoint>(settings.Points);
            Points.CollectionChanged += (s, e) => Notify();
            Notify();
        }
        private static string Format(double value) { return value.ToString("0.########", CultureInfo.CurrentCulture); }
        private void Notify()
        {
            var property = PropertyChanged; if (property != null) property(this, new PropertyChangedEventArgs(String.Empty));
            var handler = Changed; if (handler != null) handler();
        }
        public bool CanEdit { get { return canEdit; } set { canEdit = value; Notify(); } }
        public string AmountText { get { return amount; } set { if (amount != value) { amount = value; Notify(); } } }
        public string DurationText { get { return duration; } set { if (duration != value) { duration = value; Notify(); } } }
        public string VariationText { get { return variation; } set { if (variation != value) { variation = value; Notify(); } } }
        public string LimitText { get { return limit; } set { if (limit != value) { limit = value; Notify(); } } }
        public string GapText { get { return gap; } set { if (gap != value) { gap = value; Notify(); } } }
        public int CadenceIndex { get { return (int)settings.Cadence; } set { if (value >= 0 && CadenceIndex != value) { settings.Cadence = (CadenceMode)value; Notify(); } } }
        public int UnitIndex { get { return (int)settings.Unit; } set { if (value >= 0 && UnitIndex != value) { settings.Unit = (TimeUnit)value; Notify(); } } }
        public int ButtonIndex { get { return (int)settings.Button; } set { if (value >= 0 && ButtonIndex != value) { settings.Button = (ClickButton)value; Notify(); } } }
        public int ActivationIndex { get { return (int)settings.Activation; } set { if (value >= 0 && ActivationIndex != value) { settings.Activation = (ActivationMode)value; Notify(); } } }
        public int LimitIndex { get { return (int)settings.Limit; } set { if (value >= 0 && LimitIndex != value) { settings.Limit = (LimitMode)value; Notify(); } } }
        public bool VariationEnabled { get { return settings.VariationEnabled; } set { if (value != settings.VariationEnabled) { settings.VariationEnabled = value; Notify(); } } }
        public bool LimitEnabled { get { return settings.LimitEnabled; } set { if (value != settings.LimitEnabled) { settings.LimitEnabled = value; Notify(); } } }
        public bool DoubleEnabled { get { return settings.DoubleClickEnabled; } set { if (value != settings.DoubleClickEnabled) { settings.DoubleClickEnabled = value; Notify(); } } }
        public bool SequenceEnabled { get { return settings.SequenceEnabled; } set { if (value != settings.SequenceEnabled) { settings.SequenceEnabled = value; Notify(); } } }
        public bool AlwaysOnTop { get { return settings.AlwaysOnTop; } set { if (value != settings.AlwaysOnTop) { settings.AlwaysOnTop = value; Notify(); } } }
        public bool MinimizeOnStart { get { return settings.MinimizeOnStart; } set { if (value != settings.MinimizeOnStart) { settings.MinimizeOnStart = value; Notify(); } } }
        public int HotkeyKey { get { return settings.HotkeyKey; } }
        public HotkeyModifiers HotkeyMods { get { return settings.HotkeyMods; } }
        public string HotkeyText
        {
            get
            {
                string prefix = String.Empty;
                if ((settings.HotkeyMods & HotkeyModifiers.Control) != 0) prefix += "Ctrl + ";
                if ((settings.HotkeyMods & HotkeyModifiers.Alt) != 0) prefix += "Alt + ";
                if ((settings.HotkeyMods & HotkeyModifiers.Shift) != 0) prefix += "Shift + ";
                if ((settings.HotkeyMods & HotkeyModifiers.Windows) != 0) prefix += "Win + ";
                string key = KeyInterop.KeyFromVirtualKey(settings.HotkeyKey).ToString();
                if (key.Length == 2 && key[0] == 'D' && Char.IsDigit(key[1])) key = key.Substring(1);
                return prefix + key;
            }
        }
        public string CadenceConnector { get { return settings.Cadence == CadenceMode.Rate ? "per" : "every"; } }
        public string CadenceDescription { get { return settings.Cadence == CadenceMode.Rate ? "Cycles per selected time unit. Double click sends two clicks per cycle." : "Time between cycle starts. Long double click gaps can extend a cycle."; } }
        public string View { get { return settings.View; } set { settings.View = value; Notify(); } }
        public void SetHotkey(int key, HotkeyModifiers mods) { settings.HotkeyKey = key; settings.HotkeyMods = mods; Notify(); }

        public bool TryRead(bool forRun, out ClickSettings result, out string error)
        {
            result = settings.Copy();
            error = null;
            double a, d, v, l, g;
            if (!Number(amount, out a)) error = "Enter a valid rate or delay.";
            else if (!Number(duration, out d)) error = "Enter a valid click duration.";
            else if (!Number(variation, out v)) error = "Enter a valid speed variation.";
            else if (!Number(limit, out l)) error = "Enter a valid limit.";
            else if (!Number(gap, out g)) error = "Enter a valid double click gap.";
            if (error != null) return false;
            Number(amount, out a); Number(duration, out d); Number(variation, out v); Number(limit, out l); Number(gap, out g);
            result.Amount = a; result.DurationPercent = d; result.VariationPercent = v; result.LimitValue = l; result.DoubleClickGapMs = g;
            result.Points = Points.Select(p => new SequencePoint(p.X, p.Y)).ToList();
            error = result.Validate(forRun);
            return error == null;
        }

        private static bool Number(string value, out double result)
        {
            return Double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result) && !Double.IsNaN(result) && !Double.IsInfinity(result);
        }
    }
}
