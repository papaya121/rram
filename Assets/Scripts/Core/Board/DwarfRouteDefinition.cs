using System;
using System.Collections.Generic;

namespace RRaM.Core.Board
{
    [Serializable]
    public sealed class DwarfRouteDefinition
    {
        public string RouteId;
        public List<string> LowerRoute = new();
        public List<string> UpperRoute = new();
        public List<string> ReturnRoute = new();

        public List<string> BuildFullRoute()
        {
            List<string> result = new();
            Append(result, LowerRoute);
            Append(result, UpperRoute);
            Append(result, ReturnRoute);
            return result;
        }

        private static void Append(List<string> target, List<string> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (target.Count > 0 && target[target.Count - 1] == source[i])
                {
                    continue;
                }

                target.Add(source[i]);
            }
        }
    }
}
