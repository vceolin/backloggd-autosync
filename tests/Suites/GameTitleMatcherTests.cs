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
    }
}
