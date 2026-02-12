using UnityEngine;

namespace RadioDecadance.Tools
{
    public class RegexStringAttribute : PropertyAttribute
    {
        public readonly string Regex;
        public readonly string Tooltip;

        public RegexStringAttribute(string regex, string tooltip = "Field does not match required format")
        {
            Regex = regex;
            Tooltip = tooltip;
        }
    }
}
