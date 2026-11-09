using System;
using System.Collections.Generic;

namespace BackloggdAutoSync.Tests
{
    [TestFixture]
    public class GameTitleMatcherTests
    {
        [Test]
        public void Should_Clean_Trademark_And_Copyright_Symbols()
        {
            Assert.AreEqual("NieR Automata", GameTitleMatcher.CleanGameTitle("NieR™ Automata"));
            Assert.AreEqual("Pokemon Shield", GameTitleMatcher.CleanGameTitle("Pokemon® Shield©"));
        }

        [Test]
        public void Should_Clean_Dash_Edition_Suffixes()
        {
            Assert.AreEqual("The Witcher 3: Wild Hunt",
                GameTitleMatcher.CleanGameTitle("The Witcher 3: Wild Hunt - Game of the Year Edition"));

            Assert.AreEqual("Cyberpunk 2077",
                GameTitleMatcher.CleanGameTitle("Cyberpunk 2077 - Ultimate Edition"));

            Assert.AreEqual("Control",
                GameTitleMatcher.CleanGameTitle("Control - Ultimate Edition"));

            Assert.AreEqual("Persona 5 Royal",
                GameTitleMatcher.CleanGameTitle("Persona 5 Royal - Digital Deluxe Edition"));

            Assert.AreEqual("Death Stranding",
                GameTitleMatcher.CleanGameTitle("Death Stranding - Director's Cut"));
        }

        [Test]
        public void Should_Clean_Parenthesized_Edition_Suffixes()
        {
            Assert.AreEqual("Hollow Knight",
                GameTitleMatcher.CleanGameTitle("Hollow Knight (Collector's Edition)"));

            Assert.AreEqual("Resident Evil 4",
                GameTitleMatcher.CleanGameTitle("Resident Evil 4 (Deluxe Edition)"));

            Assert.AreEqual("Sekiro: Shadows Die Twice",
                GameTitleMatcher.CleanGameTitle("Sekiro: Shadows Die Twice (GOTY Edition)"));
        }

        [Test]
        public void Should_Preserve_Standard_Titles_And_Collapse_Spaces()
        {
            Assert.AreEqual("Celeste", GameTitleMatcher.CleanGameTitle("Celeste"));
            Assert.AreEqual("Super Mario Odyssey", GameTitleMatcher.CleanGameTitle("  Super   Mario   Odyssey  "));
            Assert.AreEqual("", GameTitleMatcher.CleanGameTitle(""));
            Assert.AreEqual("", GameTitleMatcher.CleanGameTitle(null));
        }

        [Test]
        public void Should_Normalize_Title()
        {
            Assert.AreEqual("grandtheftautov", GameTitleMatcher.NormalizeTitle("Grand Theft Auto V"));
            Assert.AreEqual("halflife2", GameTitleMatcher.NormalizeTitle("Half-Life 2"));
            Assert.AreEqual("nierautomata", GameTitleMatcher.NormalizeTitle("NieR: Automata™"));
            Assert.AreEqual("pokemonlegendsarceus", GameTitleMatcher.NormalizeTitle("Pokémon Legends: Arceus"));
            Assert.AreEqual("Pokemon Legends: Arceus", GameTitleMatcher.StripDiacritics("Pokémon Legends: Arceus"));
            Assert.AreEqual("", GameTitleMatcher.NormalizeTitle(""));
            Assert.AreEqual("", GameTitleMatcher.NormalizeTitle(null));
        }

        [Test]
        public void Should_Select_Exact_Match()
        {
            var results = new List<GameSearchResult>
            {
                new GameSearchResult { GameId = "101", Title = "Hades II", ReleaseYear = "2024" },
                new GameSearchResult { GameId = "100", Title = "Hades", ReleaseYear = "2020" }
            };

            var match = GameTitleMatcher.SelectBestMatch("Hades", results);
            Assert.IsNotNull(match);
            Assert.AreEqual("100", match.GameId);
        }

        [Test]
        public void Should_Select_Prefix_Match()
        {
            var results = new List<GameSearchResult>
            {
                new GameSearchResult { GameId = "200", Title = "Elden Ring: Shadow of the Erdtree", ReleaseYear = "2024" },
                new GameSearchResult { GameId = "201", Title = "Armored Core VI", ReleaseYear = "2023" }
            };

            var match = GameTitleMatcher.SelectBestMatch("Elden Ring", results);
            Assert.IsNotNull(match);
            Assert.AreEqual("200", match.GameId);
        }

        [Test]
        public void Should_Select_Single_Result_Fallback()
        {
            var results = new List<GameSearchResult>
            {
                new GameSearchResult { GameId = "300", Title = "Chrono Trigger: Radical Dreamers Edition", ReleaseYear = "1995" }
            };

            var match = GameTitleMatcher.SelectBestMatch("Chrono", results);
            Assert.IsNotNull(match);
            Assert.AreEqual("300", match.GameId);
        }

        [Test]
        public void Should_Return_Null_For_No_Or_Ambiguous_Matches()
        {
            var results = new List<GameSearchResult>
            {
                new GameSearchResult { GameId = "401", Title = "Halo: Combat Evolved" },
                new GameSearchResult { GameId = "402", Title = "Forza Horizon 5" }
            };

            var match = GameTitleMatcher.SelectBestMatch("God of War", results);
            Assert.IsNull(match);

            Assert.IsNull(GameTitleMatcher.SelectBestMatch("Anything", null));
            Assert.IsNull(GameTitleMatcher.SelectBestMatch("Anything", new List<GameSearchResult>()));
        }

        [Test]
        public void Should_Generate_Search_Query_Candidates_And_Match_Steam_Titles()
        {
            var gtaCandidates = GameTitleMatcher.GetSearchQueryCandidates("Grand Theft Auto IV: The Complete Edition");
            Assert.IsTrue(gtaCandidates.Contains("Grand Theft Auto IV"));

            var codCandidates = GameTitleMatcher.GetSearchQueryCandidates("Call of Duty: Black Ops II - Multiplayer");
            Assert.IsTrue(codCandidates.Contains("Call of Duty: Black Ops II"));

            var mafiaCandidates = GameTitleMatcher.GetSearchQueryCandidates("Mafia II (Classic)");
            Assert.IsTrue(mafiaCandidates.Contains("Mafia II"));

            var segaCandidates = GameTitleMatcher.GetSearchQueryCandidates("SEGA Mega Drive & Genesis Classics");
            Assert.IsTrue(segaCandidates.Contains("SEGA Mega Drive and Genesis Classics"));

            // Matching & vs "and"
            var segaMatch = GameTitleMatcher.SelectBestMatch("SEGA Mega Drive & Genesis Classics", new List<GameSearchResult>
            {
                new GameSearchResult { GameId = "102190", Title = "Sega Mega Drive and Genesis Classics", ReleaseYear = "2010" }
            });
            Assert.IsNotNull(segaMatch);
            Assert.AreEqual("102190", segaMatch.GameId);

            // Matching franchise prefix ("Tom Clancy's ")
            var ghostReconMatch = GameTitleMatcher.SelectBestMatch("Ghost Recon Breakpoint", new List<GameSearchResult>
            {
                new GameSearchResult { GameId = "118218", Title = "Tom Clancy's Ghost Recon: Breakpoint", ReleaseYear = "2019" },
                new GameSearchResult { GameId = "118219", Title = "Tom Clancy's Ghost Recon: Wildlands", ReleaseYear = "2017" }
            });
            Assert.IsNotNull(ghostReconMatch);
            Assert.AreEqual("118218", ghostReconMatch.GameId);

            // Matching release year in parentheses ("Resident Evil 4 (2005)")
            var re4Match = GameTitleMatcher.SelectBestMatch("Resident Evil 4 (2005)", new List<GameSearchResult>
            {
                new GameSearchResult { GameId = "196860", Title = "Resident Evil 4", ReleaseYear = "2023" },
                new GameSearchResult { GameId = "974", Title = "Resident Evil 4", ReleaseYear = "2005" }
            });
            Assert.IsNotNull(re4Match);
            Assert.AreEqual("974", re4Match.GameId);
        }
    }
}
