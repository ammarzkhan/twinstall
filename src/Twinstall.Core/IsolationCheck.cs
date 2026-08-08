using System.Collections.Generic;

namespace Twinstall.Core
{
    /// <summary>
    /// Refuses configurations where two instances would share or nest their data.
    /// This is the guard that makes "your accounts stay separate" a claim we can make.
    /// </summary>
    public static class IsolationCheck
    {
        public static IList<string> Validate(string existingProfileDir, string newProfileDir)
        {
            var issues = new List<string>();
            string a = PathUtil.Normalise(existingProfileDir);
            string b = PathUtil.Normalise(newProfileDir);

            if (a.Length == 0 || b.Length == 0)
            {
                issues.Add("a profile path is empty");
                return issues;
            }
            if (PathUtil.SamePath(a, b))
                issues.Add("both instances resolve to the same folder");
            if (PathUtil.IsInside(b, a))
                issues.Add("the new profile is inside the existing profile");
            if (PathUtil.IsInside(a, b))
                issues.Add("the existing profile is inside the new profile");

            return issues;
        }

        public static bool IsValid(string existingProfileDir, string newProfileDir)
        {
            return Validate(existingProfileDir, newProfileDir).Count == 0;
        }
    }
}
