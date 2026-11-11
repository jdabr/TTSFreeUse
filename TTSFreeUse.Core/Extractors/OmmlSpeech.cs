using System.Runtime.CompilerServices;
using DocumentFormat.OpenXml;
using TTSFreeUse.Core.Transformers;
using M = DocumentFormat.OpenXml.Math;

namespace TTSFreeUse.Core.Extractors
{
    /// <summary>
    /// Turns a Word equation (Office Math Markup, OMML) into words a voice can
    /// read: "x squared plus y squared equals z squared", "a over b",
    /// "the square root of x", "the sum from i equals 1 to n of i".
    /// Short parts are read plainly; longer ones get a comma pause after them so
    /// "the fraction a plus b, over c" stays clear when heard.
    /// </summary>
    public static class OmmlSpeech
    {
        public static string Speak(OpenXmlElement math) => MathSymbols.Tidy(Element(math));

        private static string Element(OpenXmlElement? e)
        {
            // Turns absurdly deep nesting (a damaged or malicious file) into an
            // ordinary error instead of a stack overflow, which would end the app.
            RuntimeHelpers.EnsureSufficientExecutionStack();
            return e switch
            {
                null => "",
                M.Run run => RunText(run),
                M.Fraction f => Fraction(f),
                M.Superscript s => $"{Element(s.Base)} {Power(s.SuperArgument)}",
                M.Subscript s => $"{Element(s.Base)} sub {Element(s.SubArgument)}",
                M.SubSuperscript s => $"{Element(s.Base)} sub {Element(s.SubArgument)} {Power(s.SuperArgument)}",
                M.PreSubSuper s => $"{Element(s.Base)} with pre-subscript {Element(s.SubArgument)} " +
                                   $"and pre-superscript {Element(s.SuperArgument)}",
                M.Radical r => Radical(r),
                M.Nary n => Nary(n),
                M.Delimiter d => Delimiter(d),
                M.MathFunction f => Function(f),
                M.LimitLower l => LimitLower(l),
                M.LimitUpper l => $"{Element(l.Base)} over {Element(l.Limit)}",
                M.Accent a => Accent(a),
                M.Bar b => $"{Element(b.Base)} bar",
                M.Matrix m => Matrix(m),
                M.EquationArray a => string.Join(" , ", a.Elements<M.Base>().Select(Element)) + " ,",
                // Properties hold formatting only; never read them.
                M.ControlProperties or M.RunProperties or OpenXmlLeafElement => "",
                _ when e.LocalName.EndsWith("Pr", StringComparison.Ordinal) => "",
                // Everything else (Base, OfficeMath, Box, Phantom, …): read the contents.
                _ => Children(e),
            };
        }

        private static string Children(OpenXmlElement e) =>
            string.Join(' ', e.ChildElements.Select(Element).Where(s => s.Length > 0));

        private static string RunText(M.Run run)
        {
            var text = string.Concat(run.Elements<M.Text>().Select(t => t.Text));
            // "Normal text" runs are ordinary words typed inside an equation.
            return run.GetFirstChild<M.RunProperties>()?.GetFirstChild<M.NormalText>() != null
                ? text
                : MathSymbols.Speak(text);
        }

        /// <summary>True when the spoken text is a single word or number.</summary>
        private static bool IsSimple(string spoken) => !spoken.Trim().Contains(' ');

        // A comma after a long part gives a short pause, marking where it ends.
        private static string Pause(string spoken) => IsSimple(spoken) ? spoken : spoken + " ,";

        private static string Fraction(M.Fraction f)
        {
            var num = Element(f.Numerator);
            var den = Element(f.Denominator);
            return IsSimple(num) && IsSimple(den)
                ? $"{num} over {den}"
                : $"the fraction {num} , over {Pause(den)}";
        }

        private static string Power(OpenXmlElement? exponent)
        {
            var power = MathSymbols.Tidy(Element(exponent));
            return power switch
            {
                "2" => "squared",
                "3" => "cubed",
                "prime" or "double prime" => power,
                "T" => "transpose",
                _ => $"to the power of {Pause(power)}",
            };
        }

        private static string Radical(M.Radical r)
        {
            var body = Pause(Element(r.Base));
            var degree = MathSymbols.Tidy(Element(r.Degree));
            return degree switch
            {
                "" or "2" => $"the square root of {body}",
                "3" => $"the cube root of {body}",
                _ => $"the root of order {degree} of {body}",
            };
        }

        private static string Nary(M.Nary n)
        {
            // Word stores ∫ as the default when no character is given.
            var symbol = n.NaryProperties?.GetFirstChild<M.AccentChar>()?.Val?.Value;
            var name = symbol switch
            {
                "∑" => "the sum",
                "∏" => "the product",
                "∬" => "the double integral",
                "∭" => "the triple integral",
                "∮" => "the contour integral",
                "⋃" => "the union",
                "⋂" => "the intersection",
                _ => "the integral",
            };
            var from = Element(n.SubArgument);
            var to = Element(n.SuperArgument);
            var range = (from.Length > 0 ? $" from {from}" : "") + (to.Length > 0 ? $" to {to}" : "");
            return $"{name}{range} of {Pause(Element(n.Base))}";
        }

        private static string Delimiter(M.Delimiter d)
        {
            var props = d.DelimiterProperties;
            var open = props?.BeginChar?.Val?.Value ?? "(";
            var close = props?.EndChar?.Val?.Value ?? ")";
            var parts = d.Elements<M.Base>().Select(Element).ToList();
            var inner = string.Join(" , ", parts);

            if (open == "|" && close == "|")
                return $"the absolute value of {Pause(inner)}";
            if (open == "‖" && close == "‖")
                return $"the norm of {Pause(inner)}";
            // Brackets group things; a comma on each side keeps the grouping
            // audible without saying "open parenthesis" every time.
            return IsSimple(inner) ? inner : $", {inner} ,";
        }

        private static string Function(M.MathFunction f)
        {
            // "sine of x"; "lim" written with a lower limit reads
            // "the limit as x approaches 0 of …".
            var name = MathSymbols.Tidy(Element(f.FunctionName));
            return $"{name} of {Pause(Element(f.Base))}";
        }

        private static string LimitLower(M.LimitLower l)
        {
            var name = MathSymbols.Tidy(Element(l.Base));
            var below = Element(l.Limit);
            return name == "the limit" ? $"the limit as {below}" : $"{name} over {below}";
        }

        private static string Accent(M.Accent a)
        {
            var mark = a.AccentProperties?.GetFirstChild<M.AccentChar>()?.Val?.Value ?? "̂";
            var body = Element(a.Base);
            return mark switch
            {
                "̂" or "^" => $"{body} hat",
                "̄" or "̅" or "¯" => $"{body} bar",
                "̇" => $"{body} dot",
                "̈" => $"{body} double dot",
                "⃗" or "→" => $"vector {body}",
                "̃" or "~" => $"{body} tilde",
                _ => body,
            };
        }

        private static string Matrix(M.Matrix m)
        {
            var rows = m.Elements<M.MatrixRow>().ToList();
            var spokenRows = rows.Select((row, i) =>
                $"row {i + 1} , " + string.Join(" , ", row.Elements<M.Base>().Select(Element)));
            return $"a matrix with {rows.Count} {(rows.Count == 1 ? "row" : "rows")} , " +
                   string.Join(" , ", spokenRows) + " , end matrix ,";
        }
    }
}
