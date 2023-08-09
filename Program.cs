using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AI.Dev.OpenAI.GPT;



namespace MyApp
{
    using ResultDict = System.Collections.Generic.Dictionary<string, (double score, System.Collections.Generic.HashSet<string> queries, bool IsSorry, int inputTokens, int outputTokens)>;

    internal class Program
    {
        static string idealsFilename = "e:\\tmp\\ideals.tsv";
        static string multiFilename = "e:\\tmp\\multi.tsv";

        static void Main(string[] args)
        {
            if (args.Length > 1)
            {
                ReadIdeals();
                ReadEquivalents();
                block = ReadList("block.csv");
                perfects = ReadListHash("perfects.csv");
                CreateConsole(args);
            }
            else
            {
                Search("C:\\Users\\bobgood\\Downloads");
                using (TextWriter tw = new StreamWriter(multiFilename))
                {
                    foreach (var n in multiIterationV2Utterances)
                    {
                        tw.WriteLine(n);
                    }
                }

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
                        if (!scores.ContainsKey(n) && unfilledQueries[n].Count() > 0)
                        {
                            tw.Write("*");
                            tw.Write(n);

                            int total = 0;
                            foreach (var m in unfilledQueries[n])
                            {
                                total++;
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
        static HashSet<string> multiIterationV2Utterances = new HashSet<string>();

        static void CheckInterationCount(string ruu, string json)
        {
            int cnt = 0;
            int pos = 0;
            while (pos >= 0)
            {
                pos = json.IndexOf("\"serviceName\": \"PolymerLLM\",", pos + 10);
                if (pos >= 0)
                {
                    cnt++;
                }
            }

            if (cnt > 2)
            {
                multiIterationV2Utterances.Add(ruu);
            }
        }

        static Dictionary<string, string> equivalents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static void ReadEquivalents()
        {
            using (TextReader tr = new StreamReader("equivalents.csv"))
            {
                string line;
                while (null != (line = tr.ReadLine()))
                {
                    string[] parts = line.Split(',');
                    if (parts.Length == 2)
                    {
                        equivalents[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }
        }

        static List<HashSet<string>> perfects;
        static HashSet<string> block;

        static List<HashSet<string>> ReadListHash(string fn)
        {
            List<HashSet<string>> result = new List<HashSet<string>>();
            using (TextReader tr = new StreamReader(fn))
            {
                string line;
                while (null != (line = tr.ReadLine()))
                {
                    if (line.Trim().Length == 0) continue;
                    HashSet<string> h = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in line.Split(","))
                    {
                        h.Add(p.Trim());
                    }

                    result.Add(h);
                }
            }

            return result;
        }

        static HashSet<string> ReadList(string fn)
        {
            HashSet<string> result = new HashSet<string>();
            using (TextReader tr = new StreamReader(fn))
            {
                string line;
                while (null != (line = tr.ReadLine()))
                {
                    var p = line.Trim();
                    if (p.Length > 0)
                    {
                        result.Add(p);
                    }
                }
            }

            return result;
        }

        static void ReadIdeals()
        {
            using (TextReader tr = new StreamReader("ideals.tsv"))
            {
                string line;
                while (null != (line = tr.ReadLine()))
                {
                    string[] parts = line.Split('\t');
                    string ruu = parts[0];
                    if (ruu[0] == '*')
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
        static List<string> previousTsv = new List<string>();
        static void CreateConsole(string[] args)
        {
            int treatmentCount = 0;
            int controlCount = 0;
            for (int i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                string path = arg;
                bool isTreatment = false;
                bool isAll = true;
                if (path.Length > 2 && path[1] == '=')
                {
                    if (path[0] == 't')
                    {
                        isTreatment = true;
                        isAll = false;
                    }
                    if (path[0] == 'c')
                    {
                        isTreatment = false;
                        isAll = false;
                    }

                    if (path[0] == 'p')
                    {
                        var parts = path.Split('=');
                        GetHistory(parts[2], parts[1]);
                        previousTsv.Add(path);
                        continue;
                    }

                    path = path.Substring(2).Trim();
                }


                string title;
                List<string> prefixes = new List<string>() { controlPrefix };
                if (isTreatment || isAll)
                {
                    var prefixes1 = new HashSet<string>();
                    GetTreatmentScans(path, prefixes1);
                    prefixes = prefixes1.ToList();
                    if (isAll)
                    {
                        prefixes.Add(controlPrefix);
                    }
                    prefixes.Sort();
                }


                foreach (var pre in prefixes)
                {
                    if (pre != controlPrefix)
                    {
                        title = "Treatment";
                        if (treatmentCount++ > 0)
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

                    var experiment = CreateConsole0(path, pre);
                    experiments.Add((title, experiment));
                }
            }

            experiments.Sort((x, y) => x.title.CompareTo(y.title));

            List<(string ruu, double prio)> prios = new List<(string ruu, double prio)>();
            HashSet<string> allRuus = new HashSet<string>();

            foreach (var experiment in experiments)
            {
                foreach (var ruu in experiment.dict.Keys)
                {
                    if (!block.Contains(ruu))
                    {
                        allRuus.Add(ruu);
                    }
                }

            }

            int citoken = 0;
            int cotoken = 0;
            int titoken = 0;
            int totoken = 0;
            int cqcnt = 0;
            int tqcnt = 0;
            int csorry = 0;
            int tsorry = 0;

            HashSet<string> tsorries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> csorries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                            tqcnt++;
                            titoken += line.inputTokens;
                            totoken += line.outputTokens;
                            if (line.IsSorry)
                            {
                                tsorry++;
                                tsorries.Add(ruu);
                            }
                        }
                        else
                        {
                            ctot += line.score;
                            ccnt++;
                            cqcnt++;
                            citoken += line.inputTokens;
                            cotoken += line.outputTokens;
                            if (line.IsSorry)
                            {
                                csorry++;
                                csorries.Add(ruu);
                            }
                        }
                    }

                }


                double diff = ttot / Math.Max(tcnt, 1) - ctot / (Math.Max(ccnt, 1));
                if (tcnt > 0)
                {
                    prios.Add((ruu, diff));
                }
            }


            var sortedList = prios.OrderBy(item => item.prio).ToList();

            using (TextWriter tw = new StreamWriter("e:\\tmp\\tokens.tsv"))
            {
                tw.WriteLine("\tinput tokens\toutput tokens\tsorry\ttot");
                tw.WriteLine($"control\t{(double)citoken / cqcnt:0} \t{(double)cotoken / cqcnt:0}\t{(double)csorry * 100 / cqcnt:0.0}%\t{cqcnt}");
                tw.WriteLine($"treatment\t{(double)titoken / tqcnt:0}\t{(double)totoken / tqcnt:0}\t{(double)tsorry * 100 / tqcnt:0.0}%\t{tqcnt}");
            }

            bool onlysorries = false;

            using (TextWriter tw = new StreamWriter(args[0]))
            {
                tw.WriteLine("utterance\tscore diff\tsorry\tsource\tscore\t3S query 1\t3S query 2");
                foreach (var e in sortedList)
                {
                    var ts = tsorries.Contains(e.ruu);
                    var cs = csorries.Contains(e.ruu);
                    if (onlysorries && (!ts && !cs)) continue;
                    bool first = true;
                    foreach (var experiment in experiments)
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

                            if (value.IsSorry)
                            {
                                tw.Write("Sorry");
                            }

                            tw.Write("\t");

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

                    if (history.TryGetValue(e.ruu, out List<string> lines))
                    {
                        foreach (var line in lines)
                        {
                            tw.WriteLine(line);
                        }
                    }

                    tw.WriteLine();
                }
            }
        }

        static Dictionary<string, string> Alias = new Dictionary<string, string>();
        static Dictionary<string, List<string>> history = new Dictionary<string, List<string>>();
        static void GetHistory(string path, string name)
        {
            string filename = Path.GetFileNameWithoutExtension(path);

            using (TextReader tr = new StreamReader(path))
            {
                string title = null;
                string diff = null;
                string line = tr.ReadLine();
                List<string> appendList = null;
                int ALOffset = 0;
                int scoreTotal = 0;
                int cscoreTotal = 0;
                int cscoreCnt = 0;

                while (null != (line = tr.ReadLine()))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 4)
                    {
                        title = null;
                        appendList = null;
                        continue;
                    }

                    if (parts[0].Length > 0)
                    {
                        title = null;
                    }

                    if (title == null)
                    {
                        if (parts[0].Length > 0)
                        {
                            title = parts[0];
                            diff = parts[1];
                            if (!history.TryGetValue(title, out appendList))
                            {
                                appendList = new List<string>();
                                history[title] = appendList;
                            }

                            ALOffset = appendList.Count;
                            scoreTotal = 0;
                            cscoreCnt = 0;
                            cscoreTotal = 0;
                        }
                        else if (parts[1].Length > 0)
                        {
                            title = null;
                            appendList = null;
                        }
                    }

                    if (title == null)
                    {
                        continue;
                    }

                    if (parts[2].StartsWith("Control"))
                    {
                        if (int.TryParse(parts[3], out int scorec))
                        {
                            cscoreTotal += scorec;
                            cscoreCnt++;
                        }
                        continue;
                    }

                    parts[2] = parts[2].Replace("Treatment", name);
                    if (diff != null) parts[1] = diff;
                    diff = null;
                    HashSet<string> qs = new HashSet<string>();
                    for (int i = 4; i < parts.Length; i++)
                    {
                        qs.Add(parts[i]);
                    }

                    int score = Score(title, qs);
                    scoreTotal += score;
                    parts[3] = "" + score;
                    appendList.Add(string.Join('\t', parts));

                    double cscoreAve = (double)cscoreTotal / cscoreCnt;
                    double tscoreAve = (double)(scoreTotal / (appendList.Count - ALOffset));
                    int scoreAve = (int)Math.Round(tscoreAve - cscoreAve);

                    var parts2 = appendList[ALOffset].Split('\t');
                    parts2[1] = "" + scoreAve;
                    appendList[ALOffset] = string.Join('\t', parts2);
                }
            }
        }

        static int Score(string ruu, HashSet<string> seenQueries1)
        {
            HashSet<string> seenQueries = new HashSet<string>();
            foreach (var s in seenQueries1)
            {
                if (equivalents.TryGetValue(s, out string sub))
                {
                    seenQueries.Add(sub);
                }
                else
                {
                    seenQueries.Add(s);
                }
            }

            double score = 0;
            if (ideals.TryGetValue(ruu, out var qs))
            {
                foreach (var q in qs)
                {
                    var query = q.Key;


                    if (seenQueries.Contains(query))
                    {
                        score += q.Value;
                    }
                }
            }

            int score1 = (int)(score + .5);

            foreach (var n in perfects)
            {
                bool perfect = true;
                foreach (var pq in n)
                {
                    if (!seenQueries.Contains(pq))
                    {
                        perfect = false;
                    }
                }

                if (perfect)
                {
                    score1 = 100;
                }
            }

            return score1;
        }

        static void GetTreatmentScans(string path, HashSet<string> paths)
        {

            foreach (var dir in Directory.GetDirectories(path))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith("SydneyResponses"))
                {
                    GetTreatmentScansDir(dir, paths);
                }
                else
                {
                    GetTreatmentScans(dir, paths);
                }
            }
        }

        static void GetTreatmentScansDir(string s, HashSet<string> paths)
        {
            foreach (var f in Directory.GetFiles(s))
            {
                if (Path.GetExtension(f) == ".json")
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    foreach (var n in experimentprefix)
                    {
                        if (name.StartsWith(n))
                        {
                            paths.Add(n);
                        }
                    }
                }
            }
        }

        static ResultDict CreateConsole0(string path, string prefix)
        {
            ResultDict results = new ResultDict(StringComparer.OrdinalIgnoreCase);
            CreateConsole1(path, results, prefix);
            return results;
        }

        static void CreateConsole1(string path, ResultDict results, string prefix)
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith("SydneyResponses"))
                {
                    CreateConsoleDir(dir, results, prefix);
                }
                else
                {
                    CreateConsole1(dir, results, prefix);
                }
            }
        }

        static void CreateConsoleDir(string s, ResultDict results, string prefix)
        {
            foreach (var f in Directory.GetFiles(s))
            {
                if (Path.GetExtension(f) == ".json")
                {
                    var r = GetJsonConsole(f, prefix);
                    if (r.ruu != null)
                    {
                        results[r.ruu] = (r.score, r.queries, r.IsSorry, r.inputTokens, r.outputTokens);
                    }
                }
            }
        }

        static (double score, string ruu, HashSet<string> queries, bool IsSorry, int inputTokens, int outputTokens) GetJsonConsole(string f, string prefix)
        {
            string ruu = ParseName(Path.GetFileNameWithoutExtension(f), prefix);
            if (ruu == null)
            {
                return (0, null, null, false, 0, 0);
            }

            if (!ideals.TryGetValue(ruu, out var val) || val.Count() == 0)
            {
                return (0, null, null, false, 0, 0);
            }

            string json;
            using (TextReader tr = new StreamReader(f))
            {
                json = tr.ReadToEnd();
            }

            try
            {

                var jsonObject = JsonNode.Parse(json);
                var messages = jsonObject["messages"] as JsonArray;
                var telemetry = jsonObject["telemetry"] as JsonObject;
                var metrics = telemetry["metrics"] as JsonArray;

                HashSet<string> seenQueries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (messages == null)
                {
                    return (0, null, null, false, 0, 0);
                }

                int inputTokens = 0;
                int outputTokens = 0;
                bool isSorry = false;
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

                foreach (var metric in metrics)
                {
                    if (metric["serviceName"].ToString() == "PolymerLLM")
                    {
                        var inputString = metric["input"].ToString();
                        var i1 = JsonSerializer.Deserialize<object>(inputString);

                        var doc = JsonDocument.Parse(inputString);
                        var i2 = doc.RootElement;
                        if (i2.TryGetProperty("prompt", out JsonElement valueElement))
                        {
                            string input = valueElement.ToString();
                            inputTokens += GPT3Tokenizer.Encode(input).Count();
                        }
                        var outputString = metric["output"].ToString();
                        var doco = JsonDocument.Parse(outputString);
                        var o2 = doco.RootElement;
                        if (o2.TryGetProperty("modelResponse", out JsonElement valueElement2))
                        {
                            string output = valueElement2.ToString();
                            outputTokens += GPT3Tokenizer.Encode(output).Count();
                            if (output.ToLower().Contains("i'm sorry"))
                            {
                                isSorry = true;
                            }
                        }
                    }
                }

                int score1 = Score(ruu, seenQueries);
                return (score1, ruu, seenQueries, isSorry, inputTokens, outputTokens);
            }
            catch
            {
                return (0, null, null, false, 0, 0);
            }
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
                if (Path.GetExtension(f) == ".json")
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

            if (ParseName(Path.GetFileNameWithoutExtension(f), controlPrefix) != null)
            {
                CheckInterationCount(ruu, json);
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
            while (pos >= 0)
            {
                pos = result.IndexOf("[", pos + 1);
                if (pos < 0)
                {
                    continue;
                }

                int pos2 = result.IndexOf("]", pos);
                if (pos > 0 && pos2 > 0 && result.Length > (pos2 + 1) && result[pos2 + 1] == '(')
                {
                    string between = result.Substring(pos + 1, pos2 - pos - 1);
                    if (int.TryParse(between, out _))
                    {
                        int pos3 = result.IndexOf(")", pos2);
                        if (pos3 > 0)
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

        static string controlPrefix = "control_sydney_response_";
        static List<string> experimentprefix = new List<string>()
        {
            "experiment_sydney_response_",
            "experiment2_sydney_response_",
            "experiment3_sydney_response_",
        };

        static string ParseName(string s, string prefix)
        {
            if (s.StartsWith(prefix))
            {
                return Trim(s.Substring(prefix.Length));
            }

            return null;
        }

        static string ParseName(string s)
        {
            return ParseName(s, controlPrefix) ?? ParseName(s, experimentprefix[0]) ?? ParseName(s, experimentprefix[1]) ?? ParseName(s, experimentprefix[2]);
        }

        static string Trim(string s)
        {
            return s.Replace("_", " ").Trim();
        }
    }
}