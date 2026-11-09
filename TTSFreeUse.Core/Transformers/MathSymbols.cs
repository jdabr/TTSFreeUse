using System.Text;

namespace TTSFreeUse.Core.Transformers
{
    /// <summary>
    /// Spoken words for math symbols, Greek letters, and function names, so a
    /// voice says "x is less than or equal to pi" instead of skipping symbols.
    /// </summary>
    public static class MathSymbols
    {
        private static readonly Dictionary<char, string> Symbols = new()
        {
            // Relations
            ['='] = "equals", ['≠'] = "is not equal to", ['<'] = "is less than", ['>'] = "is greater than",
            ['≤'] = "is less than or equal to", ['≥'] = "is greater than or equal to",
            ['≈'] = "is approximately", ['≅'] = "is congruent to", ['≡'] = "is equivalent to",
            ['∝'] = "is proportional to", ['∼'] = "is similar to", ['~'] = "is similar to",
            ['⊥'] = "is perpendicular to", ['∥'] = "is parallel to",
            // Operators
            ['+'] = "plus", ['−'] = "minus", ['-'] = "minus", ['±'] = "plus or minus", ['∓'] = "minus or plus",
            ['×'] = "times", ['·'] = "times", ['⋅'] = "times", ['∗'] = "times", ['*'] = "times",
            ['÷'] = "divided by", ['/'] = "over", ['√'] = "the square root of", ['∛'] = "the cube root of",
            ['!'] = "factorial", ['%'] = "percent", ['°'] = "degrees",
            ['′'] = "prime", ['″'] = "double prime",
            // Calculus
            ['∞'] = "infinity", ['∂'] = "partial", ['∇'] = "del", ['∆'] = "delta",
            ['∑'] = "the sum of", ['∏'] = "the product of", ['∫'] = "the integral of",
            ['→'] = "approaches", ['⟶'] = "approaches",
            // Sets and logic
            ['∈'] = "is in", ['∉'] = "is not in", ['⊂'] = "is a subset of", ['⊆'] = "is a subset of",
            ['∪'] = "union", ['∩'] = "intersection", ['∅'] = "the empty set",
            ['∀'] = "for all", ['∃'] = "there exists", ['¬'] = "not", ['∧'] = "and", ['∨'] = "or",
            ['⇒'] = "implies", ['⟹'] = "implies", ['⇔'] = "if and only if", ['⟺'] = "if and only if",
            ['ℝ'] = "the real numbers", ['ℕ'] = "the natural numbers", ['ℤ'] = "the integers",
            ['ℚ'] = "the rational numbers", ['ℂ'] = "the complex numbers",
            // Geometry and misc
            ['∠'] = "angle", ['△'] = "triangle", ['…'] = "and so on", ['⋯'] = "and so on",
            // Greek, lowercase
            ['α'] = "alpha", ['β'] = "beta", ['γ'] = "gamma", ['δ'] = "delta", ['ε'] = "epsilon",
            ['ϵ'] = "epsilon", ['ζ'] = "zeta", ['η'] = "eta", ['θ'] = "theta", ['ϑ'] = "theta",
            ['ι'] = "iota", ['κ'] = "kappa", ['λ'] = "lambda", ['μ'] = "mu", ['µ'] = "mu",
            ['ν'] = "nu", ['ξ'] = "xi", ['π'] = "pi", ['ρ'] = "rho", ['σ'] = "sigma", ['ς'] = "sigma",
            ['τ'] = "tau", ['υ'] = "upsilon", ['φ'] = "phi", ['ϕ'] = "phi", ['χ'] = "chi",
            ['ψ'] = "psi", ['ω'] = "omega",
            // Greek, uppercase (the ones that don't look like Latin letters)
            ['Γ'] = "capital gamma", ['Δ'] = "capital delta", ['Θ'] = "capital theta",
            ['Λ'] = "capital lambda", ['Ξ'] = "capital xi", ['Π'] = "capital pi",
            ['Σ'] = "capital sigma", ['Φ'] = "capital phi", ['Ψ'] = "capital psi",
            ['Ω'] = "capital omega",
        };

        private static readonly Dictionary<string, string> Functions = new(StringComparer.Ordinal)
        {
            ["sin"] = "sine", ["cos"] = "cosine", ["tan"] = "tangent",
            ["sec"] = "secant", ["csc"] = "cosecant", ["cot"] = "cotangent",
            ["arcsin"] = "arc sine", ["arccos"] = "arc cosine", ["arctan"] = "arc tangent",
            ["sinh"] = "hyperbolic sine", ["cosh"] = "hyperbolic cosine", ["tanh"] = "hyperbolic tangent",
            ["log"] = "log", ["ln"] = "natural log", ["exp"] = "exponential",
            ["lim"] = "the limit", ["max"] = "max", ["min"] = "min", ["det"] = "determinant",
            ["gcd"] = "greatest common divisor", ["mod"] = "mod",
        };

        /// <summary>The spoken form of a symbol, or null if it has none.</summary>
        public static string? Word(char c) => Symbols.GetValueOrDefault(c);

        /// <summary>The spoken form of a function name such as "sin", or null.</summary>
        public static string? Function(string name) => Functions.GetValueOrDefault(name);

        /// <summary>
        /// Speaks a piece of math text (for example the contents of a Word
        /// equation): symbols become words, numbers stay whole, and short runs
        /// of letters are read as separate variables ("xy" → "x y").
        /// </summary>
        public static string Speak(string text)
        {
            var words = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsDigit(c))
                {
                    // Keep "3.14" and "1,000" whole.
                    int start = i;
                    while (i < text.Length && (char.IsDigit(text[i]) ||
                           (text[i] is '.' or ',' && i + 1 < text.Length && char.IsDigit(text[i + 1]))))
                        i++;
                    words.Add(text[start..i]);
                }
                else if (char.IsLetter(c) && Word(c) == null)
                {
                    int start = i;
                    while (i < text.Length && char.IsLetter(text[i]) && Word(text[i]) == null)
                        i++;
                    var letters = text[start..i];
                    if (Function(letters) is { } function)
                        words.Add(function);
                    else if (letters.Length <= 3)
                        words.AddRange(letters.Select(l => l.ToString())); // variables: "xy" → "x y"
                    else
                        words.Add(letters); // a real word typed into the equation
                }
                else
                {
                    i++;
                    if (Word(c) is { } word)
                        words.Add(word);
                    else if (c == ',' || c == ';')
                        words.Add(",");
                    // Brackets, spaces, and anything unknown just separate words.
                }
            }
            return Tidy(string.Join(' ', words));
        }

        /// <summary>Collapses spaces and stray commas left over from joining spoken pieces.</summary>
        internal static string Tidy(string spoken)
        {
            var sb = new StringBuilder(spoken.Length);
            foreach (var part in spoken.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ",")
                {
                    if (sb.Length > 0 && sb[^1] != ',')
                        sb.Append(',');
                    continue;
                }
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(part);
            }
            return sb.ToString().Trim(',', ' ');
        }
    }
}
