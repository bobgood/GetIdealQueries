using System;
using System.Collections;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;


namespace MyApp 
{
    using RESULT = (double score, System.Collections.Generic.HashSet<string> queries);
    using ResultDict = System.Collections.Generic.Dictionary<string, (double score, System.Collections.Generic.HashSet<string> queries)>;

    internal class Program
    {
        static string idealsFilename = "e:\\tmp\\ideals.tsv";

        static void Main(string[] args)
        {
            if (args.Length >1)
            {
                ReadIdeals();
                CreateConsole(args);
            }
            else
            {
                Search("C:\\Users\\bobgood\\Downloads");
                using (TextWriter tw = new StreamWriter(idealsFilename))
                {
                    foreach (var n in scores.Keys)
                    {
                        tw.Write(n);

                        int total = 0;
                        foreach (var m in scores[n])
                        {
                            total += m.Value;
                        }

                        foreach (var m in scores[n])
                        {
                            tw.Write($"\t{m.Key}\t{100.0 * m.Value / total:0.00}");
                        }

                        tw.WriteLine();
                    }

                    foreach (var n in unfilledQueries.Keys)
                    {
                        if (!scores.ContainsKey(n) && unfilledQueries[n].Count()>0)
                        {
                            tw.Write("*");
                            tw.Write(n);

                            int total = 0;
                            foreach (var m in unfilledQueries[n])
                            {
                                total ++;
                            }

                            foreach (var m in unfilledQueries[n])
                            {
                                tw.Write($"\t{m}\t{100.0 / total:0.00}");
                            }

                            tw.WriteLine();
                        }
                    }
                }
            }
        }

        static Dictionary<string, Dictionary<string, double>> ideals = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        static HashSet<string> idealNoCitations = new HashSet<string>();

        static void ReadIdeals()
        {
            using (TextReader tr = new StreamReader("ideals.tsv"))
            {
                string line;
                while (null != (line = tr.ReadLine()))
                {
                    string[] parts = line.Split('\t');
                    string ruu = parts[0];
                    if (ruu[0]=='*')
                    {
                        ruu = ruu.Substring(1);
                        idealNoCitations.Add(ruu);
                    }

                    var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    ideals[ruu] = dict;
                    for (int i = 1; i < parts.Length; i += 2)
                    {
                        dict[parts[i]] = double.Parse(parts[i + 1]);
                    }
                }
            }
        }

        static List<(string title, ResultDict dict)> experiments = new List<(string title, ResultDict dict)>();
        static void CreateConsole(string[] args)
        {
            int treatmentCount = 0;
            int controlCount = 0;
            foreach (var arg in args)
            {
                string path = arg;
                bool isTreatment = false; 
                if (path.Length>2 && path[1]=='=')
                {
                    if (path[0] == 't')
                    {
                        isTreatment=true ;
                    }

                    path = path.Substring(2).Trim();
                }

                string title;
                if (isTreatment)
                {
                    title = "Treatment";
                    if (treatmentCount++> 0)
                    {
                        title += " " + treatmentCount;
                    }
                }
                else
                {
                    title = "Control";
                    if (controlCount++ > 0)
                    {
                        title += " " + controlCount;
                    }
                }
                var experiment = CreateConsole0(path, isTreatment);
                experiments.Add((title, experiment));
            }

            List<(string ruu, double prio)> prios = new List<(string ruu, double prio)>();
            HashSet<string> allRuus = new HashSet<string>();

            foreach (var experiment in experiments)
            {
                foreach (var ruu in experiment.dict.Keys)
                {
                    allRuus.Add(ruu);
                }

            }

            foreach (var ruu in allRuus)
            {
                double ctot = 0;
                double ccnt = 0;
                double ttot = 0;
                double tcnt = 0;
                foreach (var experiment in experiments)
                {
                    bool isTreatment = experiment.title.ToLower().StartsWith("t");
                    if (experiment.dict.TryGetValue(ruu, out var line))
                    {
                        if (isTreatment)
                        {
                            ttot += line.score;
                            tcnt++;
                        }
                        else
                        {
                            ctot += line.score;
                            ccnt++;
                        }
                    }

                }


                double diff = ttot / Math.Max(tcnt, 1) - ctot / (Math.Max(ccnt, 1));
                if (tcnt > 0 || ccnt > 0)
                {
                    prios.Add((ruu, diff));
                }
            }

            var sortedList = prios.OrderBy(item => item.prio).ToList();

            using (TextWriter tw = new StreamWriter("e:\\tmp\\report.tsv"))
            {
                tw.WriteLine("utterance\tscore diff\tsource\tscore\t3S query 1\t3S query 2");
                foreach (var e in sortedList)
                {
                    bool first = true;
                    foreach  (var experiment in experiments)
                    {
                        if (experiment.dict.TryGetValue(e.ruu, out var value))
                        {
                            if (first)
                            {
                                tw.Write($"{e.ruu}\t{Math.Round(e.prio)}\t");
                                first = false;
                            }
                            else
                            {
                                tw.Write("\t\t");
                            }

                            string asterisk = "";
                            if (idealNoCitations.Contains(e.ruu))
                            {
                                asterisk = "*";
                            }
                            tw.Write($"{experiment.title}{asterisk}\t{value.score}");
                            foreach (var q in value.queries)
                            {
                                tw.Write($"\t{q}");
                            }

                            tw.WriteLine();
                        }
                    }

                    tw.WriteLine();
                }
            }
        }

        static ResultDict CreateConsole0(string path, bool isTreatment)
        {
            ResultDict results = new ResultDict(StringComparer.OrdinalIgnoreCase);
            CreateConsole1(path, results, isTreatment);
            return results;
        }

        static void CreateConsole1(string path, ResultDict results, bool isTreatment)
        { 
            foreach (var dir in Directory.GetDirectories(path))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith("SydneyResponses"))
                {
                    CreateConsoleDir(dir, results, isTreatment);
                }
                else
                {
                    CreateConsole1(dir, results, isTreatment);
                }
            }
        }

        static void CreateConsoleDir(string s, ResultDict results, bool isTreatment)
        {
            foreach (var f in Directory.GetFiles(s))
            {
                if (Path.GetExtension(f) == ".json")
                {
                    var r = GetJsonConsole(f, isTreatment);
                    if (r.ruu != null)
                    {
                        results[r.ruu] = (r.score, r.queries);
                    }
                }
            }
        }

        static (double score, string ruu, HashSet<string> queries) GetJsonConsole(string f, bool isTreatment)
        {
            string ruu = ParseName(Path.GetFileNameWithoutExtension(f), isTreatment);
            if (ruu==null)
            {
                return (0, null, null);
            }

            if (!ideals.TryGetValue(ruu, out var val) || val.Count()==0)
            {
                return (0, null, null);
            }

            string json;
            using (TextReader tr = new StreamReader(f))
            {
                json = tr.ReadToEnd();
            }

            var jsonObject = JsonNode.Parse(json);
            var messages = jsonObject["messages"] as JsonArray;


            HashSet<string> seenQueries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (messages==null)
            {
                return (0, null, null);
            }

            foreach (var message in messages)
            {
                if (message["messageType"].ToString() == "Internal")
                {
                    foreach (var adaptiveCard in message["adaptiveCards"] as JsonArray)
                    {
                        foreach (var card in adaptiveCard["body"] as JsonArray)
                        {
                            if (card["type"].ToString() == "FactSet")
                            {
                                foreach (var fact in card["facts"] as JsonArray)
                                {
                                    if (fact["title"].ToString() == "EnterpriseSearchSearchQuery")
                                    {
                                        var query = FixQuery(fact["value"].ToString());
                                        seenQueries.Add(query);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            double score = 0;
            if (ideals.TryGetValue(ruu, out var qs))
            {
                foreach (var q in qs)
                {
                    if (seenQueries.Contains(q.Key))
                    {
                        score += q.Value;
                    }
                }
            }

            int score1 = (int)(score + .5);
            return (score1, ruu, seenQueries);
        }


        static HashSet<string> found = new HashSet<string>();

        static void Search(string s)
        {
            foreach (var dir in Directory.GetDirectories(s))
            {
                string name = Path.GetFileName(dir);
                if (Guid.TryParse(name, out Guid guid))
                {
                    if (found.Add(name))
                    {
                        Search1(dir);
                    }
                }
            }
        }

        static void Search1(string s)
        {
            foreach (var dir in Directory.GetDirectories(s))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith("SydneyResponses"))
                {
                    Search2(dir);
                }
                else
                {
                    Search1(dir);
                }
            }
        }


        static void Search2(string s)
        {
            foreach (var f in Directory.GetFiles(s))
            {
                if (Path.GetExtension(f)==".json")
                {
                    GetJson(f);
                }
            }
        }

        static Dictionary<string, HashSet<string>> unfilledQueries = new Dictionary<string, HashSet<string>>();
        static void GetJson(string f)
        {
            string ruu = ParseName(Path.GetFileNameWithoutExtension(f));

            string json;
            using (TextReader tr = new StreamReader(f))
            {
                json = tr.ReadToEnd();
            }

            var jsonObject = JsonNode.Parse(json);
            var messages = jsonObject["messages"] as JsonArray;
            var result = messages.Last()["text"].ToString();
            List<string> hits = GetHits(result);

            HashSet<string> seenQueries = new HashSet<string>();

            foreach (var message in messages)
            {
                if (message["messageType"].ToString() == "Internal")
                {
                    foreach (var adaptiveCard in message["adaptiveCards"] as JsonArray)
                    {
                        foreach (var card in adaptiveCard["body"] as JsonArray)
                        {
                            if (card["type"].ToString() == "FactSet")
                            {
                                string query = null;
                                string results = null;
                                foreach (var fact in card["facts"] as JsonArray)
                                {
                                    if (fact["title"].ToString() == "EnterpriseSearchSearchQuery")
                                    {
                                        query = FixQuery(fact["value"].ToString());
                                    }
                                    if (fact["title"].ToString() == "EnterpriseSearchDiagnostics")
                                    {
                                        results = fact["value"].ToString();
                                    }
                                }

                                if (seenQueries.Add(query))
                                {
                                    foreach (var hit in hits)
                                    {
                                        if (results.Contains(hit))
                                        {
                                            IncreaseScore(ruu, query);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (!unfilledQueries.TryGetValue(ruu, out var v))
            {
                v = new HashSet<string>();
                unfilledQueries[ruu] = v;
            }

            foreach (var q in seenQueries)
            {
                v.Add(q);
            }
        }

        static string FixQuery(string query)
        {
            string pattern = "[^a-zA-Z0-9\\s]";

           return Regex.Replace(query, pattern, "");
        }

        static List<string> GetHits(string result)
        {
            List<string> hits = new List<string>();
            int pos = 0;
            while (pos>=0)
            {
                pos = result.IndexOf("[", pos + 1);
                if (pos<0)
                {
                    continue;
                }

                int pos2 = result.IndexOf("]", pos);
                if (pos>0 && pos2 > 0 && result.Length>(pos2+1) && result[pos2+1]=='(')
                {
                    string between = result.Substring(pos + 1, pos2 - pos - 1);
                    if (int.TryParse(between, out _))
                    {
                        int pos3 = result.IndexOf(")", pos2);
                        if (pos3>0)
                        {
                            string refd = result.Substring(pos2 + 2, pos3 - pos2 - 2);
                            hits.Add(refd.Trim());
                        }
                    }
                }
            }
            return hits;
        }
        static string GetQuery(string json)
        {
            var j = JsonNode.Parse(json);
            foreach (var entity in j["AnswerEntityRequests"] as JsonArray)
            {
                var q = entity["Query"];
                return q["QueryString"].ToString();
            }

            System.Diagnostics.Debugger.Break();
            return null;

        }

        static Dictionary<string, Dictionary<string, int>> scores = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        static void IncreaseScore(string ruu, string query)
        {
            if (!scores.TryGetValue(ruu, out var scoreDict))
            {
                scoreDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                scores[ruu] = scoreDict;
            }

            if (!scoreDict.TryGetValue(query, out _))
            {
                scoreDict[query] = 0;
            }

            scoreDict[query]++;
        }

        static List<string> prefix = new List<string>()
        {
            "control_sydney_response_",
            "experiment_sydney_response_",
        };

        static string ParseName(string s)
        {
            foreach (var p in prefix)
            {
                if (s.StartsWith(p))
                {
                    return Trim(s.Substring(p.Length));
                }    
            }

            System.Diagnostics.Debugger.Break();
            return s;
        }

        static string ParseName(string s, bool isTreatment)
        {
            foreach (var p in prefix)
            {
                if (s.StartsWith(p))
                {
                    if (isTreatment && p == prefix[0]) return null;
                    if (!isTreatment && p == prefix[1]) return null;
                    return Trim(s.Substring(p.Length));
                }
            }

            System.Diagnostics.Debugger.Break();
            return s;
        }

        static string Trim(string s)
        {
            return s.Replace("_", " ").Trim();
        }
    }
}