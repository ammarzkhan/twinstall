using System;
using System.Collections.Generic;

namespace Twinstance.Core
{
    /// <summary>One folder under the profile root that might be the app's existing profile.</summary>
    public sealed class ProfileCandidate
    {
        public ProfileCandidate(string directory, int markerScore, bool hasLocalState, DateTimeOffset lastWriteUtc)
        {
            Directory = PathUtil.Normalise(directory);
            MarkerScore = markerScore;
            HasLocalState = hasLocalState;
            LastWriteUtc = lastWriteUtc;

            string parent = PathUtil.Parent(Directory);
            Name = parent.Length == 0 ? Directory : Directory.Substring(parent.Length).Trim(PathUtil.Sep);
        }

        public string Directory { get; }

        /// <summary>Leaf folder name — what gets compared against the app's own names.</summary>
        public string Name { get; }

        public int MarkerScore { get; }

        /// <summary>'Local State' is definitive; the other markers only corroborate.</summary>
        public bool HasLocalState { get; }

        public DateTimeOffset LastWriteUtc { get; }
    }

    public sealed class RankedProfile
    {
        public RankedProfile(ProfileCandidate candidate, bool nameMatched)
        {
            Candidate = candidate;
            NameMatched = nameMatched;
        }

        public ProfileCandidate Candidate { get; }

        /// <summary>The folder name equals one of the app's own names. The strongest signal we have.</summary>
        public bool NameMatched { get; }
    }

    public enum ProfileDiscoveryOutcome
    {
        /// <summary>Nothing to scan — no root, or it does not exist.</summary>
        NoRoot = 0,

        /// <summary>The app has never run. Ask the user to launch it once.</summary>
        NoneFound = 1,

        /// <summary>One clear winner. Proceed. (Not 'Single' — that collides with System.Single.)</summary>
        Unique = 2,

        /// <summary>Several, with no clear winner. Show them and let the user choose.</summary>
        Ambiguous = 3
    }

    public sealed class ProfileDiscoveryResult
    {
        public ProfileDiscoveryResult(ProfileDiscoveryOutcome outcome, IList<RankedProfile> ranked)
        {
            Outcome = outcome;
            Ranked = ranked ?? new List<RankedProfile>();
        }

        public ProfileDiscoveryOutcome Outcome { get; }

        /// <summary>Best first. Empty unless something qualified.</summary>
        public IList<RankedProfile> Ranked { get; }

        public ProfileCandidate Best
        {
            get { return Ranked.Count > 0 ? Ranked[0].Candidate : null; }
        }
    }

    /// <summary>
    /// Detection step 3: which folder under the profile root is the app's existing profile?
    ///
    /// This has to be a ranking rather than a lookup because unpackaged apps all share
    /// %APPDATA%. Measured on a real machine: scanning it for folders containing a
    /// 'Local State' returned **twelve** — every Electron app installed, from Discord to
    /// Docker Desktop. "Has Chromium markers" is therefore not a filter, it is barely a hint;
    /// the name match is what actually identifies the app.
    ///
    /// Packaged apps get a private root and typically return one or two, so the ambiguity is
    /// specific to the unpackaged branch.
    /// </summary>
    public static class ProfileDiscovery
    {
        /// <summary>
        /// Same markers the launch probe watches for — a directory is a Chromium profile on
        /// exactly the same evidence, whether we made it or the app did. Kept in one place so
        /// the two cannot drift apart.
        /// </summary>
        public static readonly string[] Markers = LaunchProbe.Markers;

        /// <summary>
        /// Names so widely shared that a match on one means nothing. Observed, not guessed:
        /// VS Code's InternalName is literally "electron", as is that of most Electron apps,
        /// and %APPDATA%\electron is a real folder on some machines. Matching it would pick a
        /// stranger's profile with full confidence.
        ///
        /// Only add to this list when something has actually been seen colliding.
        /// </summary>
        public static readonly string[] NonIdentifyingNames = { "electron", "chromium" };

        /// <summary>
        /// Names that would identify this app's own folder. Order does not matter; any exact
        /// case-insensitive hit counts.
        ///
        /// The executable's base name earns its place: VS Code's ProductName is
        /// "Visual Studio Code" but its profile folder is "Code", which only the file name
        /// matches.
        /// </summary>
        public static IList<string> IdentityNames(
            string exePath, string productName, string fileDescription, string internalName)
        {
            var names = new List<string>();
            AddName(names, BaseName(exePath));
            AddName(names, productName);
            AddName(names, fileDescription);
            AddName(names, BaseName(internalName));
            return names;
        }

        private static void AddName(List<string> into, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            string v = value.Trim();

            foreach (string generic in NonIdentifyingNames)
                if (string.Equals(generic, v, StringComparison.OrdinalIgnoreCase)) return;

            foreach (string existing in into)
                if (string.Equals(existing, v, StringComparison.OrdinalIgnoreCase)) return;

            into.Add(v);
        }

        /// <summary>File name without directory or extension.</summary>
        private static string BaseName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string n = PathUtil.Normalise(path);
            string parent = PathUtil.Parent(n);
            string leaf = parent.Length == 0 ? n : n.Substring(parent.Length).Trim(PathUtil.Sep);
            int dot = leaf.LastIndexOf('.');
            return dot > 0 ? leaf.Substring(0, dot) : leaf;
        }

        public static bool NameMatches(string folderName, IEnumerable<string> identityNames)
        {
            if (string.IsNullOrWhiteSpace(folderName) || identityNames == null) return false;
            foreach (string n in identityNames)
                if (!string.IsNullOrWhiteSpace(n) && string.Equals(n.Trim(), folderName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// Ranks candidates best-first and says whether the answer is safe to act on.
        ///
        /// A name match outranks everything, because "most recently modified" is actively
        /// misleading on a shared root — the last app you happened to use wins, and it is
        /// rarely the one being configured.
        /// </summary>
        public static ProfileDiscoveryResult Rank(
            IEnumerable<ProfileCandidate> candidates, IEnumerable<string> identityNames)
        {
            if (candidates == null) return new ProfileDiscoveryResult(ProfileDiscoveryOutcome.NoRoot, null);

            var names = new List<string>();
            if (identityNames != null) names.AddRange(identityNames);

            var qualified = new List<RankedProfile>();
            foreach (ProfileCandidate c in candidates)
            {
                if (c == null || c.MarkerScore < 1) continue;
                qualified.Add(new RankedProfile(c, NameMatches(c.Name, names)));
            }

            if (qualified.Count == 0)
                return new ProfileDiscoveryResult(ProfileDiscoveryOutcome.NoneFound, null);

            qualified.Sort(Compare);

            int matched = 0;
            foreach (RankedProfile r in qualified) if (r.NameMatched) matched++;

            ProfileDiscoveryOutcome outcome =
                qualified.Count == 1 || matched == 1
                    ? ProfileDiscoveryOutcome.Unique
                    : ProfileDiscoveryOutcome.Ambiguous;

            return new ProfileDiscoveryResult(outcome, qualified);
        }

        private static int Compare(RankedProfile a, RankedProfile b)
        {
            if (a.NameMatched != b.NameMatched) return a.NameMatched ? -1 : 1;
            if (a.Candidate.HasLocalState != b.Candidate.HasLocalState) return a.Candidate.HasLocalState ? -1 : 1;
            if (a.Candidate.MarkerScore != b.Candidate.MarkerScore) return b.Candidate.MarkerScore - a.Candidate.MarkerScore;

            int byTime = b.Candidate.LastWriteUtc.CompareTo(a.Candidate.LastWriteUtc);
            if (byTime != 0) return byTime;

            // Deterministic tail, so the same input never produces two different orders.
            return string.Compare(a.Candidate.Name, b.Candidate.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
