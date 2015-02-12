using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml.XPath;

namespace EmuScraper
{
    class Program
    {
        private const int PlatformId = 6;
        private const string PlatformName = "Super Nintendo (SNES)";

        public const string MappingFileName = @"D:\temp\romscraping\gamemapping.txt";

        public const bool DumpMapping = true;

        static void Main(string[] args)
        {
            const bool ForceUpdateGameList = false;

            var romList = ReadRomList(@"C:\Users\fparadis2\Documents\romlist.txt");

            var gameList = UpdateGameList(@"D:\temp\gamelist.xml", ForceUpdateGameList);

            if (DumpMapping)
            {
                List<GameResult> mapping = UpdateGameMapping(romList, gameList);
                DumpGameMapping(mapping, MappingFileName);
            }
        }

        private static IList<string> ReadRomList(string file)
        {
            return File.ReadAllLines(file);
        }

        private static IList<SimpleGameInfo> UpdateGameList(string path, bool forceUpdate)
        {
            if (!File.Exists(path) || forceUpdate)
            {
                var webRequest = HttpWebRequest.Create(string.Format("http://thegamesdb.net/api/GetPlatformGames.php?platform={0}", PlatformId));
                var response = webRequest.GetResponse();

                using (var responseStream = response.GetResponseStream())
                using (var outputStream = File.Create(path))
                {
                    responseStream.CopyTo(outputStream);
                }
            }

            using (var stream = File.Open(path, FileMode.Open))
            {
                var gameListDocument = new XPathDocument(stream).CreateNavigator();

                List<SimpleGameInfo> games = new List<SimpleGameInfo>();

                foreach (XPathNavigator gameNode in gameListDocument.Select("//Game"))
                {
                    SimpleGameInfo gameInfo = new SimpleGameInfo
                    {
                        Id = int.Parse(ReadElement(gameNode, "id")),
                        Name = ReadElement(gameNode, "GameTitle")
                    };

                    games.Add(gameInfo);
                }

                return games;
            }
        }

        private static string ReadElement(XPathNavigator parent, string name)
        {
            return parent.SelectSingleNode(name).Value;
        }

        private static List<GameResult> UpdateGameMapping(IEnumerable<string> romList, IList<SimpleGameInfo> gameList)
        {
            List<GameResult> results = new List<GameResult>();

            foreach (var rom in romList)
            {
                GameResult result = new GameResult { RomName = rom, ConfidenceLevel = "[NOT FOUND]"};

                // First try to find a direct match against the whole game list
                if (!FindGameInList(gameList, result))
                {
                    // If not found, do a search on thegamesdb and use the first result
                    SearchGame(result);
                }

                results.Add(result);
            }

            return results;
        }

        private static bool FindGameInList(IEnumerable<SimpleGameInfo> gameList, GameResult result)
        {
            var rom = NormalizeRomName(result.RomName);

            List<SimpleGameInfo> candidates = new List<SimpleGameInfo>();

            foreach (var game in gameList)
            {
                if (string.Equals(game.Name, rom, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Clear();
                    candidates.Add(game);
                    break;
                }

                if (game.Name.IndexOf(rom, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    candidates.Add(game);
                }
            }

            if (candidates.Count > 1)
            {
                OutputValidationInfo(rom, candidates[0].Name);
                result.Info = candidates[0];
                result.ConfidenceLevel = "[First Candidate]";
                return true;
            }

            if (candidates.Count == 1)
            {
                result.Info = candidates[0];
                result.ConfidenceLevel = "[PERFECTO]";
                return true;
            }

            //Debug.WriteLine(string.Format("Could not find game for {0}", rom));
            return false;
        }

        private static void SearchGame(GameResult result)
        {
            var rom = NormalizeRomName(result.RomName);
            rom = rom.Replace(",", "");
            rom = rom.Replace("&", " ");

            var encodedRom = rom; //HttpUtility.HtmlEncode(rom);
            var encodedPlatform = PlatformName; //HttpUtility.HtmlEncode(PlatformName);

            var webRequest = HttpWebRequest.Create(string.Format("http://thegamesdb.net/api/GetGame.php?name={0}&platform={1}", encodedRom, encodedPlatform));
            var response = webRequest.GetResponse();

            using (var responseStream = response.GetResponseStream())
            {
                var gameListDocument = new XPathDocument(responseStream).CreateNavigator();

                List<SimpleGameInfo> games = new List<SimpleGameInfo>();

                foreach (XPathNavigator gameNode in gameListDocument.Select("//Game"))
                {
                    result.Info = new SimpleGameInfo
                    {
                        Id = int.Parse(ReadElement(gameNode, "id")),
                        Name = ReadElement(gameNode, "GameTitle")
                    };

                    result.ConfidenceLevel = "[First Search Result]";

                    OutputValidationInfo(rom, result.Info.Name);
                    return;
                }
            }
        }

        private static void DumpGameMapping(IList<GameResult> mapping, string file)
        {
            var confidenceGroups = mapping.GroupBy(result => result.ConfidenceLevel);

            using (var stream = File.Create(file))
            using (var writer = new StreamWriter(stream))
            {
                foreach (var group in confidenceGroups)
                {
                    writer.WriteLine("######### {0} #########", group.Key);

                    foreach (var result in group)
                    {
                        var gameInfo = result.Info;

                        if (string.IsNullOrEmpty(gameInfo.Name))
                        {
                            writer.WriteLine("## {0} ==> ??", result.RomName);
                        }
                        else
                        {
                            writer.WriteLine("{0} ==> {1} ==> {2}", result.RomName, gameInfo.Id, gameInfo.Name);
                        }
                    }

                    writer.WriteLine();
                    writer.WriteLine();
                    writer.WriteLine();
                    writer.WriteLine();
                }
            }
        }

        private static readonly Regex RomLanguageRegex = new Regex(@"\(.?\)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string NormalizeRomName(string rom)
        {
            var match = RomLanguageRegex.Match(rom);
            if (match.Success)
            {
                rom = rom.Substring(0, match.Index);
            }

            return rom.Trim();
        }

        private static void OutputValidationInfo(string romName, string result)
        {
            Debug.WriteLine("{0} => {1}", romName, result);
        }

        private class GameResult
        {
            public string RomName;
            public SimpleGameInfo Info;
            public string ConfidenceLevel;
        }

        private struct SimpleGameInfo
        {
            public int Id;
            public string Name;
        }

        private class GameInfo
        {
            public string RomPath;
            public string Name;
            public string Description;
            public string ImagePath;
            public string Genre;
            public int Players;

            public float Rating;
            public DateTime ReleaseDate;

            public string Developer;
            public string Publisher;
        }
    }
}
