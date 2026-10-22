using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace PrinterToolkit
{
    internal enum CmdLang { Generic, ZPL, EPL, TSPL, ESCPOS }

    /// <summary>Everything the app knows about a detected printer model.</summary>
    internal class PrinterProfile
    {
        public string Brand = "Unknown";
        public string BrandFull = "Unknown manufacturer";
        public string Model = "Unknown model";
        public string Category = "Printer";
        public string Dpi = "";
        public CmdLang Language = CmdLang.Generic;
        public string SupportUrl = "";
        public string DriverSearchUrl = "";
        public bool IsVirtual;
        public bool IsKnownBrand;
        public List<string> Keywords = new List<string>();

        public string LanguageName
        {
            get
            {
                switch (Language)
                {
                    case CmdLang.ZPL: return "ZPL II";
                    case CmdLang.EPL: return "EPL2";
                    case CmdLang.TSPL: return "TSPL";
                    case CmdLang.ESCPOS: return "ESC/POS";
                    default: return IsVirtual ? "Virtual" : "Windows GDI / PCL";
                }
            }
        }

        public string DisplayName { get { return IsKnownBrand ? Brand + " " + Model : Model; } }
    }

    /// <summary>A raw printer-language command offered for a specific model family.</summary>
    internal class RawCommand
    {
        public string Title, Glyph, Payload, Description;
        public bool Dangerous;
        public RawCommand(string title, string glyph, string payload, string desc, bool dangerous)
        {
            Title = title; Glyph = glyph; Payload = payload; Description = desc; Dangerous = dangerous;
        }
    }

    internal static class ModelDatabase
    {
        private class BrandRule
        {
            public string Brand, Full, Pattern, Support, Category;
            public CmdLang Lang;
            public string[] Keys;
            public BrandRule(string brand, string full, string pattern, string support, CmdLang lang, string category, params string[] keys)
            {
                Brand = brand; Full = full; Pattern = pattern; Support = support; Lang = lang; Category = category; Keys = keys;
            }
        }

        private const string Label = "Thermal Label Printer";
        private const string Receipt = "Thermal Receipt (POS) Printer";
        private const string Office = "Office Printer";

        // Order matters: specific label / POS brands before generic office brands.
        private static readonly BrandRule[] Rules =
        {
            new BrandRule("Virtual", "Software / virtual printer", @"microsoft print to pdf|microsoft xps|onenote|^fax$|adobe pdf|pdf24|cutepdf|snagit|anydesk|bullzip", "", CmdLang.Generic, "Virtual Printer"),
            new BrandRule("Zebra", "Zebra Technologies", @"zebra|zdesigner|\b(zd|zt|zq|zc|ze|zm|gk|gx|gc)\s?\d{3}|\btlp\s?\d{4}|\blp\s?28\d\d", "https://www.zebra.com/us/en/support-downloads.html", CmdLang.ZPL, Label, "zebra", "zdesigner"),
            new BrandRule("TSC", "TSC Auto ID Technology", @"\btsc\b|\bttp-|\btdp-|\bte[23]\d\d\b|\bda2\d\d\b|\bta[23]\d\d\b", "https://www.tscprinters.com/EN/support", CmdLang.TSPL, Label, "tsc"),
            new BrandRule("Honeywell", "Honeywell / Intermec", @"honeywell|intermec|\bpc4[23]|\bpm4[235]|\bpd4[35]|\bpx[46]", "https://sps.honeywell.com/us/en/support/productivity/printers", CmdLang.Generic, Label, "honeywell", "intermec"),
            new BrandRule("Datamax", "Datamax-O'Neil", @"datamax|o'?neil", "https://sps.honeywell.com/us/en/support/productivity/printers", CmdLang.Generic, Label, "datamax"),
            new BrandRule("SATO", "SATO Corporation", @"\bsato\b|\bcl[46]nx", "https://www.sato-global.com/drivers/", CmdLang.Generic, Label, "sato"),
            new BrandRule("Godex", "GoDEX International", @"godex", "https://www.godexintl.com/downloads", CmdLang.Generic, Label, "godex"),
            new BrandRule("DYMO", "DYMO", @"dymo|labelwriter", "https://www.dymo.com/support", CmdLang.Generic, Label, "dymo"),
            new BrandRule("Brother", "Brother Industries", @"brother|\bql-\d|\btd-\d|\bpt-\d|\bhl-|\bmfc-|\bdcp-", "https://support.brother.com", CmdLang.Generic, Office, "brother"),
            new BrandRule("Epson", "Seiko Epson (POS)", @"epson\s*tm|\btm-[a-z]?\d", "https://epson.com/support", CmdLang.ESCPOS, Receipt, "epson", "tm-"),
            new BrandRule("Star", "Star Micronics", @"\bstar\b|\btsp\d{3}|mc-print", "https://starmicronics.com/support/", CmdLang.ESCPOS, Receipt, "star"),
            new BrandRule("Bixolon", "BIXOLON", @"bixolon|\bsrp-", "https://www.bixolon.com/download_view.php", CmdLang.ESCPOS, Receipt, "bixolon"),
            new BrandRule("Xprinter", "Xprinter", @"xprinter|\bxp-[0-9a-z]+", "https://www.xprintertech.com/download", CmdLang.ESCPOS, Receipt, "xprinter", "xp-"),
            new BrandRule("Citizen", "Citizen Systems", @"citizen|\bct-s\d", "https://www.citizen-systems.com/en/support", CmdLang.ESCPOS, Receipt, "citizen"),
            new BrandRule("Rongta", "Rongta Technology", @"rongta", "https://www.rongtatech.com/download", CmdLang.ESCPOS, Receipt, "rongta"),
            new BrandRule("POS-80", "Generic POS-80 / POS-58", @"\bpos-?(80|58)\b", "", CmdLang.ESCPOS, Receipt, "pos"),
            new BrandRule("Epson", "Seiko Epson", @"epson|ecotank|workforce", "https://epson.com/support", CmdLang.Generic, Office, "epson"),
            new BrandRule("HP", "HP Inc.", @"\bhp\b|hewlett|laserjet|deskjet|officejet|envy|smart tank|pagewide|designjet", "https://support.hp.com/drivers", CmdLang.Generic, Office, "hp", "hewlett"),
            new BrandRule("Canon", "Canon Inc.", @"canon|pixma|imageclass|i-sensys|imagerunner|maxify", "https://www.usa.canon.com/support", CmdLang.Generic, Office, "canon"),
            new BrandRule("Kyocera", "Kyocera Document Solutions", @"kyocera|ecosys|taskalfa", "https://www.kyoceradocumentsolutions.com/download/", CmdLang.Generic, Office, "kyocera"),
            new BrandRule("Ricoh", "Ricoh", @"ricoh|aficio", "https://support.ricoh.com", CmdLang.Generic, Office, "ricoh"),
            new BrandRule("Xerox", "Xerox", @"xerox|versalink|altalink|workcentre|phaser", "https://www.support.xerox.com", CmdLang.Generic, Office, "xerox"),
            new BrandRule("Lexmark", "Lexmark", @"lexmark", "https://support.lexmark.com", CmdLang.Generic, Office, "lexmark"),
            new BrandRule("Samsung", "Samsung (HP)", @"samsung", "https://support.hp.com/drivers", CmdLang.Generic, Office, "samsung"),
            new BrandRule("Konica Minolta", "Konica Minolta", @"konica|bizhub", "https://www.konicaminolta.com/global-en/download/", CmdLang.Generic, Office, "konica"),
            new BrandRule("Sharp", "Sharp", @"\bsharp\b", "https://global.sharp/support/", CmdLang.Generic, Office, "sharp"),
            new BrandRule("Toshiba", "Toshiba", @"toshiba|e-studio", "https://www.toshibatec.com/support/", CmdLang.Generic, Office, "toshiba"),
            new BrandRule("OKI", "OKI", @"\boki\b", "https://www.oki.com/printing/support/", CmdLang.Generic, Office, "oki"),
            new BrandRule("Pantum", "Pantum", @"pantum", "https://global.pantum.com/download/", CmdLang.Generic, Office, "pantum"),
        };

        private static readonly Regex ZebraModel = new Regex(
            @"\b(ZD\d{3}[dt]?|ZT\d{3}|ZQ\d{3}|ZC\d{3}|ZE\d{3}|ZM\d{3}|GK\d{3}[dt]?|GX\d{3}[dt]?|GC\d{3}[dt]?|TLP\s?\d{4}(-Z)?|LP\s?\d{4}(-Z)?|ZXP\s?\d|105SL|110Xi4|QLn\d{3}|iMZ\d{3})",
            RegexOptions.IgnoreCase);
        private static readonly Regex DpiRx = new Regex(@"(\d{3})\s?dpi", RegexOptions.IgnoreCase);

        /// <summary>Detects brand, model, category and command language from queue name, driver name and PnP name.</summary>
        public static PrinterProfile Detect(string name, string driver, string pnpName)
        {
            string hay = ((name ?? "") + " | " + (driver ?? "") + " | " + (pnpName ?? "")).Trim();
            var p = new PrinterProfile();

            BrandRule rule = null;
            foreach (var r in Rules)
            {
                if (Regex.IsMatch(hay, r.Pattern, RegexOptions.IgnoreCase)) { rule = r; break; }
            }

            var dm = DpiRx.Match(hay);
            if (dm.Success) p.Dpi = dm.Groups[1].Value + " dpi";

            if (rule == null)
            {
                p.Model = CleanModel(driver, name, null);
                p.Category = Regex.IsMatch(hay, "generic|text only", RegexOptions.IgnoreCase) ? "Generic driver" : "Printer";
                p.DriverSearchUrl = Google(p.Model + " printer driver download");
                p.Keywords.Add(FirstToken(p.Model));
                return p;
            }

            p.Brand = rule.Brand;
            p.BrandFull = rule.Full;
            p.Language = rule.Lang;
            p.Category = rule.Category;
            p.SupportUrl = rule.Support;
            p.IsVirtual = rule.Brand == "Virtual";
            p.IsKnownBrand = !p.IsVirtual;
            p.Keywords.AddRange(rule.Keys);

            if (rule.Brand == "Zebra")
            {
                var m = ZebraModel.Match(hay);
                string model = m.Success ? NormalizeZebra(m.Value) : CleanModel(driver, name, rule);
                p.Model = model;
                string upper = model.ToUpperInvariant();
                bool epl = Regex.IsMatch(hay, @"\bEPL\b", RegexOptions.IgnoreCase) ||
                           (Regex.IsMatch(upper, @"^T?LP ?28\d\d$") && !upper.EndsWith("-Z"));
                p.Language = epl ? CmdLang.EPL : CmdLang.ZPL;

                string cat = "desktop";
                if (Regex.IsMatch(upper, @"^(ZT|ZM|105|110)")) { cat = "industrial"; p.Category = "Industrial Thermal Label Printer"; }
                else if (Regex.IsMatch(upper, @"^(ZQ|QLN|IMZ)")) { cat = "mobile"; p.Category = "Mobile Thermal Printer"; }
                else if (Regex.IsMatch(upper, @"^(ZC|ZXP)")) { cat = "card"; p.Category = "ID Card Printer"; }
                else if (upper.StartsWith("ZE")) { cat = "print-engines"; p.Category = "Print Engine"; }
                else p.Category = "Desktop Thermal Label Printer";

                string slug = Regex.Replace(model.ToLowerInvariant(), @"[\s]", "");
                if (slug.StartsWith("zd")) slug = Regex.Replace(slug, "[dt]$", "");
                p.SupportUrl = "https://www.zebra.com/us/en/support-downloads/printers/" + cat + "/" + slug + ".html";
            }
            else
            {
                p.Model = CleanModel(driver, name, rule);
                if (rule.Category == Office)
                {
                    if (Regex.IsMatch(hay, @"laser|ecosys|bizhub|taskalfa|imagerunner|versalink|i-sensys|\bhl-", RegexOptions.IgnoreCase)) p.Category = "Laser Printer";
                    else if (Regex.IsMatch(hay, @"deskjet|inkjet|pixma|ecotank|smart tank|officejet|envy|maxify|workforce", RegexOptions.IgnoreCase)) p.Category = "Inkjet Printer";
                    if (Regex.IsMatch(hay, @"\b(ql|td)-\d", RegexOptions.IgnoreCase)) p.Category = Label;
                }
            }

            p.Keywords.Add(FirstToken(p.Model));
            if (p.IsVirtual) p.Model = name;
            p.DriverSearchUrl = Google(p.BrandFull + " " + p.Model + " driver download");
            if (string.IsNullOrEmpty(p.SupportUrl)) p.SupportUrl = p.DriverSearchUrl;
            return p;
        }

        private static string NormalizeZebra(string v)
        {
            v = Regex.Replace(v.Trim(), @"\s+", "").ToUpperInvariant();
            if (Regex.IsMatch(v, @"^(ZD|GK|GX|GC)\d{3}[DT]$")) v = v.Substring(0, v.Length - 1) + char.ToLowerInvariant(v[v.Length - 1]);
            return v;
        }

        private static string CleanModel(string driver, string name, BrandRule rule)
        {
            string src = !string.IsNullOrEmpty(driver) && !Regex.IsMatch(driver, "generic|class driver|microsoft", RegexOptions.IgnoreCase) ? driver : name;
            if (string.IsNullOrEmpty(src)) src = name ?? "Unknown";
            string s = src;
            s = Regex.Replace(s, @"\(copy \d+\)|\(redirected \d+\)|\bseries\b|\bclass driver\b|\bPCL\s?-?\d*e?\b|\bPS\b|\bXL\b|\bUFR\s?II\b|\bUFRII\b|\bLIPSLX\b|\bKX\b|\bV4\b|\bZDesigner\b|\b\d{3}\s?dpi\b|\bZPL\b|\bEPL\b|\bReceipt\b|\bdriver\b|\bprinter\b", " ", RegexOptions.IgnoreCase);
            if (rule != null)
                s = Regex.Replace(s, @"\b(" + Regex.Escape(rule.Brand) + @"|hewlett[- ]packard|seiko)\b", " ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"[\-_]+\s*$", "");
            s = Regex.Replace(s, @"\s{2,}", " ").Trim(' ', '-', '_', ',');
            return string.IsNullOrEmpty(s) ? src.Trim() : s;
        }

        private static string FirstToken(string model)
        {
            if (string.IsNullOrEmpty(model)) return "";
            foreach (var t in model.Split(' '))
                if (Regex.IsMatch(t, @"\d")) return t;
            return model.Split(' ')[0];
        }

        private static string Google(string q) { return "https://www.google.com/search?q=" + Uri.EscapeDataString(q); }

        /// <summary>Model-specific raw commands (sent directly to the printer).</summary>
        public static List<RawCommand> CommandsFor(PrinterProfile p)
        {
            var list = new List<RawCommand>();
            string model = p.Model;
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            switch (p.Language)
            {
                case CmdLang.ZPL:
                    list.Add(new RawCommand("Print Config Label", Glyph.Document, "~WC", "Prints the printer configuration / status label (~WC).", false));
                    list.Add(new RawCommand("Print Network Label", Glyph.Network, "~WL", "Prints the network configuration label (~WL).", false));
                    list.Add(new RawCommand("Calibrate Media", Glyph.Sync, "~JC", "Auto-calibrates media and ribbon sensors (~JC).", false));
                    list.Add(new RawCommand("Sensor Profile", Glyph.Diagnostic, "~JG", "Prints the media sensor profile graph (~JG).", false));
                    list.Add(new RawCommand("Print Test Label", Glyph.Printer,
                        "^XA^CI28^FO30,25^A0N,34,34^FDPrinter Toolkit Test^FS^FO30,70^A0N,26,26^FD" + model + "  " + stamp + "^FS^FO30,110^BY2^BCN,70,Y,N,N^FD" + model + "-OK^FS^XZ",
                        "Prints a test label with text and a Code-128 barcode.", false));
                    list.Add(new RawCommand("Feed Blank Label", Glyph.Upload, "^XA^FO10,10^FD ^FS^XZ", "Feeds one blank label.", false));
                    list.Add(new RawCommand("Direct Thermal Mode", Glyph.Settings, "^XA^MTD^JUS^XZ", "Sets media type to direct thermal and saves (^MTD).", false));
                    list.Add(new RawCommand("Thermal Transfer Mode", Glyph.Settings, "^XA^MTT^JUS^XZ", "Sets media type to thermal transfer (ribbon) and saves (^MTT).", false));
                    list.Add(new RawCommand("Clear Printer Buffer", Glyph.Cancel, "~JA", "Cancels all formats in the printer buffer (~JA).", false));
                    list.Add(new RawCommand("Power-On Reset", Glyph.Refresh, "~JR", "Resets the printer like a power cycle (~JR).", true));
                    list.Add(new RawCommand("Factory Defaults", Glyph.Warning, "^XA^JUF^JUS^XZ", "Restores factory default settings and saves (^JUF). Network settings are kept.", true));
                    break;
                case CmdLang.EPL:
                    list.Add(new RawCommand("Print Config Label", Glyph.Document, "\r\nU\r\n", "Prints the EPL configuration (U).", false));
                    list.Add(new RawCommand("AutoSense Calibrate", Glyph.Sync, "\r\nxa\r\n", "Runs media AutoSense calibration (xa).", false));
                    list.Add(new RawCommand("Print Test Label", Glyph.Printer,
                        "\r\nN\r\nA40,20,0,4,1,1,N,\"Printer Toolkit Test\"\r\nA40,60,0,3,1,1,N,\"" + model + " " + stamp + "\"\r\nB40,100,0,1,2,6,60,B,\"OK123\"\r\nP1\r\n",
                        "Prints a test label with text and barcode.", false));
                    list.Add(new RawCommand("Reset Printer", Glyph.Refresh, "\r\n^@\r\n", "Software reset (^@).", true));
                    break;
                case CmdLang.TSPL:
                    list.Add(new RawCommand("Self Test Page", Glyph.Document, "SELFTEST\r\n", "Prints the self test / configuration page.", false));
                    list.Add(new RawCommand("Gap Calibration", Glyph.Sync, "GAPDETECT\r\n", "Auto-detects label gap (GAPDETECT).", false));
                    list.Add(new RawCommand("Feed Label", Glyph.Upload, "FORMFEED\r\n", "Feeds one label.", false));
                    list.Add(new RawCommand("Print Test Label", Glyph.Printer,
                        "SIZE 50 mm,30 mm\r\nGAP 2 mm,0\r\nCLS\r\nTEXT 20,20,\"3\",0,1,1,\"Printer Toolkit Test\"\r\nTEXT 20,60,\"2\",0,1,1,\"" + model + " " + stamp + "\"\r\nBARCODE 20,100,\"128\",60,1,0,2,2,\"OK123\"\r\nPRINT 1\r\n",
                        "Prints a 50x30 mm test label.", false));
                    list.Add(new RawCommand("Factory Reset", Glyph.Warning, "INITIALPRINTER\r\n", "Restores factory defaults (INITIALPRINTER).", true));
                    break;
                case CmdLang.ESCPOS:
                    list.Add(new RawCommand("Print Test Receipt", Glyph.Printer,
                        "\u001B@\u001Ba\u0001\u001BE\u0001PRINTER TOOLKIT TEST\n\u001BE\u0000" + model + "\n" + stamp + "\n--------------------------------\nIf you can read this, the\nprinter and driver are working.\n\n\n\n\u001DVB\u0000",
                        "Prints a short test receipt and cuts.", false));
                    list.Add(new RawCommand("Feed 5 Lines", Glyph.Upload, "\u001Bd\u0005", "Feeds paper 5 lines (ESC d).", false));
                    list.Add(new RawCommand("Cut Paper", Glyph.Cancel, "\u001DVB\u0000", "Partial cut with feed (GS V).", false));
                    list.Add(new RawCommand("Open Cash Drawer", Glyph.Package, "\u001Bp\u0000\u0019\u00FA", "Kicks the cash drawer on pin 2 (ESC p).", false));
                    list.Add(new RawCommand("Initialize Printer", Glyph.Refresh, "\u001B@", "Resets printer formatting (ESC @).", false));
                    break;
            }
            return list;
        }

        /// <summary>Converts user-typed text with escapes (\r \n \t \xHH) to bytes.</summary>
        public static byte[] ParseEscapes(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == 'r') { sb.Append('\r'); i++; continue; }
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                    if (n == 't') { sb.Append('\t'); i++; continue; }
                    if (n == '\\') { sb.Append('\\'); i++; continue; }
                    if (n == 'x' && i + 3 < s.Length)
                    {
                        int v;
                        if (int.TryParse(s.Substring(i + 2, 2), System.Globalization.NumberStyles.HexNumber, null, out v))
                        { sb.Append((char)v); i += 3; continue; }
                    }
                }
                sb.Append(c);
            }
            return Encoding.GetEncoding(28591).GetBytes(sb.ToString());
        }
    }
}
