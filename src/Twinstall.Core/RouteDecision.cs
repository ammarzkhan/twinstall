using System.Collections.Generic;

namespace Twinstall.Core
{
    public enum RouteReason
    {
        NoInstances,
        OnlyOneRunning,
        ZOrder,
        NeedsPrompt,
        FallbackFirst
    }

    public sealed class RouteResult
    {
        /// <summary>Null when <see cref="Reason"/> is NeedsPrompt or NoInstances.</summary>
        public string Target { get; set; }

        public RouteReason Reason { get; set; }
    }

    /// <summary>
    /// Decides which instance should receive an incoming deep link. Pure: no Win32, no I/O,
    /// so the behaviour that actually matters is unit-testable.
    ///
    /// The window highest in the Z-order belongs to the instance the user was last in, and
    /// that ordering survives the browser taking focus during sign-in. That is the case that
    /// fires in practice; the others are fallbacks.
    /// </summary>
    public static class RouteDecision
    {
        public static RouteResult Choose(
            IList<Instance> instances,
            IDictionary<int, string> runningPidToInstance,
            IList<int> pidsTopDown)
        {
            if (instances == null || instances.Count == 0)
                return new RouteResult { Reason = RouteReason.NoInstances };

            var running = runningPidToInstance ?? new Dictionary<int, string>();

            var distinct = new List<string>();
            foreach (var kv in running)
                if (!distinct.Contains(kv.Value)) distinct.Add(kv.Value);

            if (distinct.Count > 1 && pidsTopDown != null)
            {
                foreach (int pid in pidsTopDown)
                {
                    string name;
                    if (running.TryGetValue(pid, out name))
                        return new RouteResult { Target = name, Reason = RouteReason.ZOrder };
                }
            }

            if (distinct.Count == 1)
                return new RouteResult { Target = distinct[0], Reason = RouteReason.OnlyOneRunning };

            if (instances.Count == 1)
                return new RouteResult { Target = instances[0].Name, Reason = RouteReason.FallbackFirst };

            return new RouteResult { Reason = RouteReason.NeedsPrompt };
        }
    }
}
