using System;
using System.Text.RegularExpressions;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using System.Xml;
using System.Xml.Serialization;

namespace DuplicateProceduresView
{
    public class Procedure
    {
        public int HeaderLineIndex { get; set; }
        public int BodyLineIndex { get; set; }
        public string OldName { get {
                var m = Regex.Match(OldHeader, @"[ \t]*((FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)(\s*(\(.*?\)))?(\s*PIPELINED)?)\s*;", RegexOptions.Singleline);
                if(m.Success) { return m.Groups[3].Value; }
                else { return "Sorry, I could not extract the header name."; }
            } }
        public string OldBody { get; set; }
        public string NewName { get {
                var m = Regex.Match(NewHeader, @"[ \t]*((FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)(\s*(\(.*?\)))?(\s*PIPELINED)?)\s*;", RegexOptions.Singleline);
                if (m.Success) { return m.Groups[1].Value; }
                else { return "Sorry, I could not extract the header name."; }
            } }
        private string n; 
        public string NewBody { get; set; }
        public string OldHeader { get
            {
                var m = Regex.Match(OldBody, @"[ \t]*((FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)(\s*(\(.*?\)))?(\s*PIPELINED)?)\s+(IS|AS)\s+(.*?)END(\s+\3)\s*;", RegexOptions.Singleline);
                if(m.Success) { return m.Groups[1].Value + ";"; }
                else { return "Sorry, I could not extract the header definition."; }
            } }
        public string NewHeader { get
            {
                var m = Regex.Match(NewBody, @"[ \t]*((FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)(\s*(\(.*?\)))?(\s*PIPELINED)?)\s+(IS|AS)\s+(.*?)END(\s+\3)\s*;", RegexOptions.Singleline);
                if(m.Success) { return m.Groups[1].Value + ";"; }
                else { return "Sorry, I could not extract the header definition."; }
            } }
        public string Color { get; set; }
        public bool ShouldBeCopied { get; set; } = true;
    }

    public partial class MainWindow : Window
    {
        public string bodyFile = null;
        public string headerFile = null;
        public Config Config { get; set; } = null;
        public string OldHeader = "";
        public string OldBody = "";
        public string BodyRegex { get; } =
            @"[ \t]*((FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)(\s*(\(.*?\)))?(\s*PIPELINED)?)\s+(IS|AS)\s+(.*?)END(\s+\3)\s*;";

        List<Procedure> Procedures = new List<Procedure>();

        public static string HeaderRegex(string procedureName)
        {
            return @"([ \t]*(FUNCTION|PROCEDURE)\s+" + procedureName + @"\s*(\(.*?\))?\s*(PIPELINED)?\s*;)\s*";
        }

        public MainWindow()
        {
            InitializeComponent();
            WindowState = WindowState.Maximized;
            using (FileStream xmlStream = new FileStream("config.xml", FileMode.Open))
            {
                using (XmlReader xmlReader = XmlReader.Create(xmlStream))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(Config));
                    Config = serializer.Deserialize(xmlReader) as Config;
                }
            }
        }

        private void tbSearchProcedure_TextChanged(object sender, TextChangedEventArgs e)
        {
            foreach (var procedure in Procedures)
            {
                procedure.ShouldBeCopied = procedure.OldName.Contains(tbSearchProcedure.Text);
                ProceduresChanged();
            }
        }

        public IEnumerable<Procedure> getProcedures(string bodyFile, string headerFile)
        {
            OldBody = File.ReadAllText(bodyFile);
            var body = OldBody;
            OldHeader = File.ReadAllText(headerFile);
            var header = OldHeader;
            var procedures = new List<Procedure>();
            int searchFrom = 0;
            while(true)
            {
                var bodyMatch = Regex.Match(body, BodyRegex, RegexOptions.Singleline);
                if (bodyMatch.Success)
                {
                    var headerMatch = Regex.Match(OldHeader, HeaderRegex(bodyMatch.Groups[3].Value));
                    var procedureText = body.Substring(bodyMatch.Index, bodyMatch.Length);
                    yield return
                        new Procedure
                        { OldBody = procedureText,
                          NewBody = procedureText,
                          BodyLineIndex = bodyMatch.Index + bodyMatch.Length + searchFrom,
                          HeaderLineIndex = headerMatch.Index + headerMatch.Length
                        };
                    searchFrom = bodyMatch.Index + bodyMatch.Length;
                    body = body.Substring(bodyMatch.Index + bodyMatch.Length);
                    searchFrom = bodyMatch.Index + bodyMatch.Length;
                }
                else
                {
                    break;
                }
            }
        }

       
        private void ProceduresChanged()
        {
            pnlProcedures.Children.Clear();
            foreach (var procedure in Procedures)
            {
                if (procedure.ShouldBeCopied)
                {
                    var editProcedure = new EditProcedure();
                    editProcedure.t1.DataContext = procedure;
                    editProcedure.t2.DataContext = procedure;
                    pnlProcedures.Children.Add(editProcedure);
                }
            }

            dgProcedures.ItemsSource = Procedures;
            dgProcedures.SelectedIndex = -1;
            dgProcedures.Items.Refresh();
        }


        private void dgProcedures_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedProcedure = dgProcedures.SelectedItem as Procedure;
            if (selectedProcedure == null) { return; }
            selectedProcedure.ShouldBeCopied = !selectedProcedure.ShouldBeCopied;
            ProceduresChanged();
        }

        private void btnSaveFiles_Click(object sender, RoutedEventArgs e)
        {
            var body = OldBody;
            int currentIndex = 0;
            foreach (var procedure in Procedures.Where(x => x.ShouldBeCopied).OrderByDescending(x => x.BodyLineIndex))
            {
                body =
                    body.Substring(currentIndex, procedure.BodyLineIndex)
                    + "\r\n\r\n\r\n"
                    + procedure.NewBody
                    + body.Substring(procedure.BodyLineIndex);
            }
            File.WriteAllText(bodyFile + "_1", body);

            var header = OldHeader;
            currentIndex = 0;
            foreach (var procedure in Procedures.Where(x => x.ShouldBeCopied).OrderByDescending(x => x.HeaderLineIndex))
            {
                header =
                    header.Substring(currentIndex, procedure.HeaderLineIndex)
                    + procedure.NewHeader
                    + "\r\n"
                    + header.Substring(procedure.HeaderLineIndex);
            }
            File.WriteAllText(headerFile + "_1", header);
            MessageBox.Show("Your files have been written.\r\nThank you for using Ederer Productivity Tools.\r\nThe program will exit.");
        }

        private string Replace(string text, string search, string replace, bool similar)
        {
            if (similar)
            {
                return
                    (text ?? "")
                        .Replace(search, replace)
                        .Replace(search.ToLower(), replace.ToLower())
                        .Replace(search.ToUpper(), replace.ToUpper());
            }
            else
            {
                return (text ?? "").Replace(search, replace);
            }
        }

        private void btnReplace_Click(object sender, RoutedEventArgs e)
        {
            foreach(var x in pnlProcedures.Children)
            {
                var editProcedure = x as EditProcedure;
                if(editProcedure == null) { continue; }
                var t1 = editProcedure.t1;
                if (rbReplaceEverywhere.IsChecked ?? true)
                {
                    t1.Text = Replace(t1.Text, tbSearch.Text, tbReplace.Text, cbReplaceSimilarMatches.IsChecked ?? true);
                }
                else if (rbReplaceSelected.IsChecked ?? false)
                {
                    var textBefore = t1.Text.Substring(0, t1.SelectionStart);
                    var textAfter = t1.Text.Substring(t1.SelectionStart + t1.SelectionLength);
                    t1.Text =
                        textBefore
                        + Replace(
                            t1.Text.Substring(t1.SelectionStart, t1.SelectionLength),
                            tbSearch.Text,
                            tbReplace.Text,
                            cbReplaceSimilarMatches.IsChecked ?? true)
                        + textAfter;
                }
            }
        }

        private void tbHeader_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                if (e.Key != Key.Enter) { return; }
                var p = tbHeader.Text.Trim().ToLower();
                if (Regex.IsMatch(p, "^[a-zA-Z0-9_-]+$"))
                {
                    p = Config.remap.Where(x => x.from == p).Select(x => x.to).FirstOrDefault() ?? p;
                    var workingDir = Config.Schema.First(schema => Regex.IsMatch(p, schema.regex)).workingDir;
                    tbHeader.Text = Path.Combine(workingDir, "ph" + p + ".sql");
                    tbBody.Text = Path.Combine(workingDir, "bh" + p + ".sql");
                }
                Procedures = getProcedures(tbBody.Text, tbHeader.Text).ToList();
                dgProcedures.ItemsSource = Procedures;
                ProceduresChanged();
            }
            catch(Exception err)
            {
                MessageBox.Show("Sorry, an error occured: " + err.ToString());
            }

        }

        private void tbBody_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key != Key.Enter) { return; }
                var p = tbBody.Text.Trim().ToLower();
                if(Regex.IsMatch(p, "p_(.*)?(_v?7$|$)?"))
                {
                    p = p.Substring(2);
                }
                if (Regex.IsMatch(p, "^[a-zA-Z0-9_-]+$"))
                {
                    p = Config.remap.Where(x => x.from == p).Select(x => x.to).FirstOrDefault() ?? p;
                    var workingDir = Config.Schema.First(schema => Regex.IsMatch(p, schema.regex)).workingDir;
                    tbHeader.Text = Path.Combine(workingDir, "ph" + p + ".sql");
                    tbBody.Text = Path.Combine(workingDir, "bh" + p + ".sql");
                }
                Procedures = getProcedures(tbBody.Text, tbHeader.Text).ToList();
                dgProcedures.ItemsSource = Procedures;
                ProceduresChanged();
                bodyFile = tbBody.Text;
                headerFile = tbHeader.Text;
            }
            catch(Exception err)
            {
                MessageBox.Show("Sorry, an error occured: " + err.ToString());
                bodyFile = null;
                headerFile = null;
            }
        }
    }
}
