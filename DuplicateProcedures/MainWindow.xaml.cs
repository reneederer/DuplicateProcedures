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

namespace DuplicateProcedures
{
    public class Procedure
    {
        public int HeaderLineBeginIndex { get; set; }
        public int BodyLineBeginIndex { get; set; }
        public int HeaderLineEndIndex { get; set; }
        public int BodyLineEndIndex { get; set; }
        public string OldName { get {
                var m = Regex.Match(OldHeader, @"[ \t]*((FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)(\s*(\(.*?\)))?(\s*PIPELINED)?)\s*;", RegexOptions.Singleline);
                return m.Groups[3].Value;
            } }
        public string OldBody { get; set; }
        public string NewName { get {
                var m = Regex.Match(NewHeader, @"[ \t]*((FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)(\s*(\(.*?\)))?(\s*PIPELINED)?)\s*;", RegexOptions.Singleline);
                return m.Groups[3].Value;
            } }
        public string NewBody { get; set; }
        public string OldHeader { get
            {
                var m = Regex.Match(OldBody, @"[ \t]*((FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)(\s*(\(.*?\)))?(\s*PIPELINED)?)\s+(IS|AS)\s+(.*?)END(\s+\3)\s*;", RegexOptions.Singleline);
                return m.Groups[1].Value + ";";
            } }
        public string NewHeader { get
            {
                var m = Regex.Match(NewBody, @"[ \t]*((FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)(\s*(\(.*?\)))?(\s*PIPELINED)?)\s+(IS|AS)\s+(.*?)END(\s+\3)\s*;", RegexOptions.Singleline);
                return m.Groups[1].Value + ";";
            } }
        public string Color { get; set; }
        public bool ShouldBeCopied { get; set; } = true;
    }


    public partial class MainWindow : Window
    {
        public Manager Manager { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            WindowState = WindowState.Maximized;
            Manager = new Manager();
            RoutedCommand copyToTbSearchCmd = new RoutedCommand();
            copyToTbSearchCmd.InputGestures.Add(new KeyGesture(Key.F3, ModifierKeys.None));
            CommandBindings.Add(new CommandBinding(copyToTbSearchCmd, tbSearch_SetClipboard));

            RoutedCommand copyToTbReplaceCmd = new RoutedCommand();
            copyToTbReplaceCmd.InputGestures.Add(new KeyGesture(Key.F4, ModifierKeys.None));
            CommandBindings.Add(new CommandBinding(copyToTbReplaceCmd, tbReplace_SetClipboard));

            RoutedCommand replaceCmd = new RoutedCommand();
            replaceCmd.InputGestures.Add(new KeyGesture(Key.F5, ModifierKeys.None));
            CommandBindings.Add(new CommandBinding(replaceCmd, btnReplace_Click));
        }

        private void tbSearch_SetClipboard(object sender, RoutedEventArgs e)
        {
            ApplicationCommands.Copy.Execute(null, null);
            tbSearch.Text = Clipboard.GetText();
        }

        private void tbReplace_SetClipboard(object sender, RoutedEventArgs e)
        {
            ApplicationCommands.Copy.Execute(null, null);
            tbReplace.Text = Clipboard.GetText();
        }

        private void tbSearchProcedure_TextChanged(object sender, TextChangedEventArgs e)
        {
            foreach (var procedure in Manager.Procedures)
            {
                Manager.SetShouldBeCopied(tbSearchProcedure.Text);
                ProceduresChanged();
            }
        }
       
        private void ProceduresChanged()
        {
            pnlProcedures.Children.Clear();
            foreach (var procedure in Manager.Procedures)
            {
                if (procedure.ShouldBeCopied)
                {
                    var editProcedure = new EditProcedure();
                    double textBoxHeight = 400.0;
                    double.TryParse(Manager.Config.TextboxHeight ?? "400.0", out textBoxHeight);
                    editProcedure.t1.Height = textBoxHeight;
                    editProcedure.t2.Height = textBoxHeight;
                    editProcedure.t1.DataContext = procedure;
                    editProcedure.t2.DataContext = procedure;
                    pnlProcedures.Children.Add(editProcedure);
                }
            }
            dgProcedures.ItemsSource = Manager.Procedures;
            dgProcedures.SelectedIndex = -1;
            dgProcedures.Items.Refresh();
            btnSaveFiles.IsEnabled = Manager.Procedures.Any(x => x.ShouldBeCopied);
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
            try
            {
                var nonFixableDuplicates = Manager.GetNonFixableDuplicates();
                if (nonFixableDuplicates.Any())
                {
                    MessageBox.Show("You are trying to create multiple new definitions for \r\n\t" + String.Join(",\r\n\t", nonFixableDuplicates.Select(x => x.NewName).Distinct()) + ".\r\nSorry, I can't decide which ones to use.", "Multiple definitions found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                var fixableDuplicates = Manager.GetFixableDuplicates();
                if (fixableDuplicates.Any())
                {
                    var r = MessageBox.Show("There already exist definitions for \r\n\t\"" + String.Join("\",\r\n\t\"", fixableDuplicates.Select(x => x.NewName).Distinct()) + "\".\r\nDo you want to overwrite them?", "Multiple definitions found", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                    if (r == MessageBoxResult.Cancel || r == MessageBoxResult.No)
                    {
                        return;
                    }
                }
                Manager.SaveFiles(fixableDuplicates);
                MessageBox.Show("Your files have been written.\r\nThank you for using Ederer Productivity Tools.\r\nThe program will exit.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Shutdown();
            }
            catch(Exception err)
            {
                MessageBox.Show("Sorry, an error occurred:\r\n\r\n" + err.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string Replace(string text, string search, string replace, bool similar)
        {
            Func<string, string> camelCaseToUnderscoreUpperCase = (s) => Regex.Replace(s, "([^A-Z])([A-Z])", "$1_$2").ToUpper();
            Func<string, string> firstToUpper = (s) => s.Length == 0 ? "" : (s[0].ToString().ToUpper() + s.Substring(1));
            Func<string, string> firstToLower = (s) => s.Length == 0 ? "" : (s[0].ToString().ToLower() + s.Substring(1));

            if(String.IsNullOrEmpty(text) || string.IsNullOrEmpty(search) || string.IsNullOrEmpty(replace))
            {
                return text;
            }

            if (similar)
            {
                var s = camelCaseToUnderscoreUpperCase(search);
                return
                    text
                        .Replace(search, replace)
                        .Replace(search.ToLower(), replace.ToLower())
                        .Replace(search.ToUpper(), replace.ToUpper())
                        .Replace(camelCaseToUnderscoreUpperCase(search), camelCaseToUnderscoreUpperCase(replace))
                        .Replace(firstToLower(search), firstToLower(replace))
                        .Replace(firstToUpper(search), firstToUpper(replace));
            }
            else
            {
                return (text ?? "").Replace(search, replace);
            }
        }
        private void Replace()
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

        private void btnReplace_Click(object sender, RoutedEventArgs e)
        {
            Replace();
        }

        private void SetHeaderAndBody(string s)
        {
            try
            {
                if (Regex.IsMatch(s, "^[a-zA-Z0-9_-]+$"))
                {
                    s = s.Trim().ToLower();
                    s = Manager.Config.Remap.Where(x => x.from == s).Select(x => x.to).FirstOrDefault() ?? s;
                    var workingDir = Manager.Config.Schema.First(schema => Regex.IsMatch(s, schema.regex)).workingDir;
                    tbHeader.Text = Path.Combine(workingDir, "ph" + s + ".sql");
                    tbBody.Text = Path.Combine(workingDir, "bh" + s + ".sql");
                }
                Manager.ReadProcedures(tbHeader.Text, tbBody.Text);
                dgProcedures.ItemsSource = Manager.Procedures;
                ProceduresChanged();
            }
            catch(Exception err)
            {
                MessageBox.Show("Sorry, an error occured: " + err.ToString());
            }
        }

        private void tbHeader_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if(e.Key != Key.Enter) { return; }
            SetHeaderAndBody(tbHeader.Text);
        }

        private void tbBody_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.Key != Key.Enter) { return; }
            SetHeaderAndBody(tbBody.Text);
        }

        private void tbSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.Key != Key.Enter) { return; }
            Replace();
        }

        private void tbReplace_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.Key != Key.Enter) { return; }
            Replace();
        }
    }
}
