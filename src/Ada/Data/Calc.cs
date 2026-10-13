using System.Globalization;

namespace Ada.Data;

/// <summary>Spotlight hesap makinesi: + - * / % ^ ( ), ondalık (. veya ,), "x" çarpı, tekli eksi.</summary>
public static class Calc
{
    public static bool TryEval(string input, out double result)
    {
        result = 0;
        var s = input.Replace(" ", "").Replace('x', '*').Replace('×', '*').Replace('÷', '/').Replace(',', '.');
        if (s.Length == 0 || !s.Any(ch => "+-*/%^(".Contains(ch)) || !s.Any(char.IsDigit)) return false;
        if (s.Any(ch => !(char.IsDigit(ch) || "+-*/%^().".Contains(ch)))) return false;
        int pos = 0;
        try
        {
            result = Expr(s, ref pos);
            return pos == s.Length && double.IsFinite(result);
        }
        catch { return false; }
    }

    static double Expr(string s, ref int p)
    {
        double v = Term(s, ref p);
        while (p < s.Length && (s[p] == '+' || s[p] == '-'))
        {
            char op = s[p++];
            double r = Term(s, ref p);
            v = op == '+' ? v + r : v - r;
        }
        return v;
    }

    static double Term(string s, ref int p)
    {
        double v = Power(s, ref p);
        while (p < s.Length && (s[p] == '*' || s[p] == '/' || s[p] == '%'))
        {
            char op = s[p++];
            double r = Power(s, ref p);
            v = op == '*' ? v * r : op == '/' ? v / r : v % r;
        }
        return v;
    }

    static double Power(string s, ref int p)
    {
        double b = Unary(s, ref p);
        if (p < s.Length && s[p] == '^') { p++; return Math.Pow(b, Power(s, ref p)); } // sağdan birleşir
        return b;
    }

    static double Unary(string s, ref int p)
    {
        if (p < s.Length && s[p] == '-') { p++; return -Unary(s, ref p); }
        if (p < s.Length && s[p] == '+') { p++; return Unary(s, ref p); }
        return Atom(s, ref p);
    }

    static double Atom(string s, ref int p)
    {
        if (p < s.Length && s[p] == '(')
        {
            p++;
            double v = Expr(s, ref p);
            if (p >= s.Length || s[p] != ')') throw new FormatException();
            p++;
            return v;
        }
        int start = p;
        while (p < s.Length && (char.IsDigit(s[p]) || s[p] == '.')) p++;
        if (start == p) throw new FormatException();
        return double.Parse(s[start..p], CultureInfo.InvariantCulture);
    }

    public static string Format(double v)
    {
        if (Math.Abs(v) >= 1e15 || (Math.Abs(v) < 1e-6 && v != 0)) return v.ToString("0.######E+0", CultureInfo.GetCultureInfo("tr-TR"));
        return Math.Round(v, 10).ToString("#,0.##########", CultureInfo.GetCultureInfo("tr-TR"));
    }
}
