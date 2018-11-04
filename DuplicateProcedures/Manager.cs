using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using System.Text.RegularExpressions;

namespace DuplicateProcedures
{
    public class Manager
    {
        public string bodyFile = null;
        public string headerFile = null;
        public Config Config { get; set; } = null;
        public string OldHeader = "";
        public string OldBody = "";
        public string NewHeader = "";
        public string NewBody = "";
        public List<Procedure> Procedures { get; set; } = new List<Procedure>();
        public static string BodyRegex { get; } =
            @"[ \t]*((FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)(\s*(\(.*?\)))?(\s*PIPELINED)?)\s+(IS|AS)\s+(.*?)END(\s+\3)\s*;";
        public static string HeaderRegex(string procedureName) =>
            @"([ \t]*(FUNCTION|PROCEDURE)\s+" + procedureName + @"\s*(\(.*?\))?\s*(PIPELINED)?\s*;)";
        public Manager()
        {
            using (FileStream xmlStream = new FileStream("config.xml", FileMode.Open))
            {
                using (XmlReader xmlReader = XmlReader.Create(xmlStream))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(Config));
                    Config = serializer.Deserialize(xmlReader) as Config;
                }
            }
        }


        public void ReadProcedures(string hFile, string bFile)
        {
            bodyFile = bFile;
            headerFile = hFile;
            OldBody = File.ReadAllText(bodyFile);
            NewBody = OldBody;
            OldHeader = File.ReadAllText(headerFile);
            NewHeader = OldHeader;
            var header = OldHeader;
            SetProcedures();
        }

        public void SetProcedures()
        {
            var body = NewBody;
            var header = NewHeader;
            Procedures = new List<Procedure>();
            int searchFrom = 0;
            while (searchFrom <= body.Length)
            {
                var bodyMatch = Regex.Match(body.Substring(searchFrom), BodyRegex, RegexOptions.Singleline);
                if (bodyMatch.Success)
                {
                    var headerMatch = Regex.Match(header, HeaderRegex(bodyMatch.Groups[3].Value));
                    var procedureText = body.Substring(searchFrom + bodyMatch.Index, bodyMatch.Length);
                    Procedures.Add(
                        new Procedure
                        { OldBody = procedureText,
                          NewBody = procedureText,
                          BodyLineBeginIndex = bodyMatch.Index + searchFrom,
                          HeaderLineBeginIndex = headerMatch.Index,
                          BodyLineEndIndex = bodyMatch.Length + bodyMatch.Index + searchFrom,
                          HeaderLineEndIndex = headerMatch.Index + headerMatch.Length
                        });
                    searchFrom += bodyMatch.Index + bodyMatch.Length;
                }
                else
                {
                    break;
                }
            }
        }

        public void SetShouldBeCopied(string text)
        {
            foreach (var procedure in Procedures)
            {
                procedure.ShouldBeCopied = procedure.OldName.Contains(text);
            }
        }

        public IEnumerable<Procedure> GetFixableDuplicates()
        {
            return
                Procedures.Where(x =>
                    x.ShouldBeCopied
                    && ((x.NewName == x.OldName && x.NewBody != x.OldBody)
                        || Procedures.Any(y =>
                                (x != y) && (x.NewName == y.OldName || (y.ShouldBeCopied ? x.NewName == y.NewName : false))
                            )
                        )
                );
        }

        public IEnumerable<Procedure> GetNonFixableDuplicates()
        {
            return
                Procedures.Where(x =>
                    x.ShouldBeCopied
                    && Procedures.Any(y =>
                        y.ShouldBeCopied && (x.NewBody != y.NewBody) && (x.NewName == y.NewName))
                );
        }

        public void ReplaceProcedure(Procedure p)
        {
            var headerLengthDiff = p.NewHeader.Length - (p.HeaderLineEndIndex - p.HeaderLineBeginIndex);
            var bodyLengthDiff = p.NewBody.Length - (p.BodyLineEndIndex - p.BodyLineBeginIndex);

            NewHeader =
                NewHeader.Substring(0, p.HeaderLineBeginIndex)
                + p.NewHeader
                + NewHeader.Substring(p.HeaderLineEndIndex);
            NewBody =
                NewBody.Substring(0, p.BodyLineBeginIndex)
                + p.NewBody
                + NewBody.Substring(p.BodyLineEndIndex);

            foreach (var procedure in Procedures.Where(x => x.ShouldBeCopied && x != p))
            {
                if (procedure.HeaderLineBeginIndex > p.HeaderLineBeginIndex)
                {
                    procedure.HeaderLineBeginIndex += headerLengthDiff;
                    procedure.HeaderLineEndIndex += headerLengthDiff;
                }
                if (procedure.BodyLineBeginIndex > p.BodyLineBeginIndex)
                {
                    procedure.BodyLineBeginIndex += bodyLengthDiff;
                    procedure.BodyLineEndIndex += bodyLengthDiff;
                }
            }
            p.HeaderLineEndIndex = p.HeaderLineBeginIndex + p.NewHeader.Length;
            p.BodyLineEndIndex = p.BodyLineBeginIndex + p.NewBody.Length;
        }

        public void SaveFiles(IEnumerable<Procedure> fixableDuplicates)
        {
            foreach(var procedure in fixableDuplicates)
            {
                ReplaceProcedure(procedure);
                procedure.ShouldBeCopied = false;
            }

            var body = NewBody;
            var header = NewHeader;

            foreach (var procedure in Procedures.Where(x => x.ShouldBeCopied).OrderByDescending(x => x.BodyLineEndIndex))
            {
                body =
                    body.Substring(0, procedure.BodyLineEndIndex)
                    + "\r\n\r\n"
                    + procedure.NewBody
                    + body.Substring(procedure.BodyLineEndIndex);
            }

            foreach (var procedure in Procedures.Where(x => x.ShouldBeCopied).OrderByDescending(x => x.HeaderLineEndIndex))
            {
                header =
                    header.Substring(0, procedure.HeaderLineEndIndex)
                    + "\r\n"
                    + procedure.NewHeader
                    + header.Substring(procedure.HeaderLineEndIndex);
            }

            File.WriteAllText(bodyFile + "_1", body);
            File.WriteAllText(headerFile + "_1", header);
            }
        }
    }
