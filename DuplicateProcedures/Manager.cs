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
    public enum Action
    {
        None,
        Create,
        Replace
    }
    public class Procedure
    {
        public string BodyPrefix { get; set; }
        public int HeaderIndex { get; set; }
        public int BodyIndex { get; set; }

        public string OldName
        {
            get
            {
                var m = Regex.Match(OldHeader, @"[ \t]*((FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)(\s*(\(.*?\)))?(\s*PIPELINED)?)\s*;", RegexOptions.Singleline);
                return m.Groups[3].Value;
            }
        }
        public string NewName
        {
            get
            {
                var m = Regex.Match(NewHeader, @"[ \t]*((FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)(\s*(\(.*?\)))?(\s*PIPELINED)?)\s*;", RegexOptions.Singleline);
                return m.Groups[3].Value;
            }
        }
        public string OldBody { get; set; }
        public string NewBody { get; set; }

        public string HeaderPrefix { get; set; }
        public string OldHeader
        {
            get
            {
                var m = Regex.Match(OldBody, @"[ \t]*((FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)(\s*(\(.*?\)))?(\s*PIPELINED)?)\s+(IS|AS)\s+(.*?)END(\s+\3)\s*;", RegexOptions.Singleline);
                return m.Groups[1].Value + ";";
            }
        }
        public string NewHeader
        {
            get
            {
                var m = Regex.Match(NewBody, @"[ \t]*((FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)(\s*(\(.*?\)))?(\s*PIPELINED)?)\s+(IS|AS)\s+(.*?)END(\s+\3)\s*;", RegexOptions.Singleline);
                return m.Groups[1].Value + ";";
            }
        }
        public Action MyAction { get; set; } = Action.None;
        public bool ShouldBeCreated => (MyAction == Action.Create);
    }
    public class Manager
    {
        public string bodyFile = null;
        public string headerFile = null;
        public Config Config { get; set; } = null;
        public string OldHeader = "";
        public string OldBody = "";
        public string NewHeader = "";
        public string NewBody = "";
        public string BodySuffix = "";
        public string HeaderSuffix = "";
        public List<Procedure> Procedures { get; set; } = new List<Procedure>();
        public static string BodyRegex { get; } =
            @"[ \t]*((FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)(\s*(\(.*?\)))?(\s*PIPELINED)?)\s+(IS|AS)\s+(.*?)END(\s+\3)\s*;";
        public static string HeaderRegex =>
            @"([ \t]*(FUNCTION|PROCEDURE)\s+([a-zA-Z0-9_-]*?)\s*(\(.*?\))?\s*(PIPELINED)?\s*;)";
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
            var bodyMatches = Regex.Matches(body, BodyRegex, RegexOptions.Singleline);
            var headerMatches = Regex.Matches(header, HeaderRegex, RegexOptions.Singleline);
            for (int bodyIndex = 0; bodyIndex < bodyMatches.Count; ++bodyIndex)
            {
                var bodyMatch = bodyMatches[bodyIndex];
                Match headerMatch = null;
                int headerIndex = 0;
                for (; headerIndex < headerMatches.Count; ++headerIndex)
                {
                    if (headerMatches[headerIndex].Groups[3].Value == bodyMatch.Groups[3].Value)
                    {
                        headerMatch = headerMatches[headerIndex];
                        break;
                    }

                }
                var bodyPrefixBegin =
                    (bodyIndex == 0)
                        ? 0
                        : bodyMatches[bodyIndex - 1].Index + bodyMatches[bodyIndex - 1].Length;
                var headerPrefixBegin =
                    (headerIndex == 0)
                        ? 0
                        : headerMatches[headerIndex - 1].Index + headerMatches[headerIndex - 1].Length;
                Procedures.Add(
                    new Procedure
                    {
                        BodyPrefix = body.Substring(bodyPrefixBegin, bodyMatch.Index - bodyPrefixBegin),
                        OldBody = bodyMatch.Value,
                        NewBody = bodyMatch.Value,
                        BodyIndex = bodyIndex,
                        HeaderPrefix =
                            header.Substring(headerPrefixBegin, headerMatch.Index - headerPrefixBegin),
                        HeaderIndex = headerIndex,
                        MyAction = Action.None
                    });
            }
            BodySuffix = body.Substring(bodyMatches[bodyMatches.Count - 1].Index + bodyMatches[bodyMatches.Count - 1].Length);
            HeaderSuffix = header.Substring(headerMatches[headerMatches.Count - 1].Index + headerMatches[headerMatches.Count - 1].Length);
        }

        public void SetCreate(string text)
        {
            if (string.IsNullOrEmpty(text)) { return; }
            foreach (var procedure in Procedures)
            {
                procedure.MyAction = procedure.OldName.Contains(text) ? Action.Create : Action.None;
            }
        }

        public IEnumerable<Procedure> GetFixableDuplicates()
        {
            return
                Procedures.Where(x =>
                    x.MyAction == Action.Create
                    && ((x.NewName == x.OldName && x.NewBody != x.OldBody)
                        || Procedures.Any(y =>
                                (x != y) && (x.NewName == y.OldName || (y.MyAction == Action.Create ? x.NewName == y.NewName : false))
                            )
                        )
                );
        }

        public IEnumerable<Procedure> GetNonFixableDuplicates()
        {
            return
                Procedures.Where(x =>
                    x.MyAction == Action.Create
                    && Procedures.Any(y =>
                        y.MyAction == Action.Create && (x.NewBody != y.NewBody) && (x.NewName == y.NewName))
                );
        }

        public void SaveFiles()
        {
            var body = "";
            foreach (var procedure in Procedures.OrderBy(x => x.BodyIndex))
            {
                if (procedure.MyAction == Action.None)
                {
                    body = body + procedure.BodyPrefix + procedure.OldBody;
                }
                if (procedure.MyAction == Action.Replace)
                {
                    body = body + procedure.BodyPrefix + procedure.NewBody;
                }
                if (procedure.MyAction == Action.Create)
                {
                    body = body + procedure.BodyPrefix + procedure.OldBody;
                    body = body + "\r\n\r\n\r\n" + procedure.NewBody;
                }
            }

            var header = "";
            foreach (var procedure in Procedures.OrderBy(x => x.HeaderIndex))
            {
                if (procedure.MyAction == Action.None)
                {
                    header = header + procedure.HeaderPrefix + procedure.OldHeader;
                }
                if (procedure.MyAction == Action.Replace)
                {
                    header = header + procedure.HeaderPrefix + procedure.NewHeader;
                }
                if (procedure.MyAction == Action.Create)
                {
                    header = header + procedure.HeaderPrefix + procedure.OldHeader;
                    header = header + "\r\n" + procedure.NewHeader;
                }
            }

            File.WriteAllText(bodyFile + "_1", body + BodySuffix);
            File.WriteAllText(headerFile + "_1", header + HeaderSuffix);
        }
    }
}
