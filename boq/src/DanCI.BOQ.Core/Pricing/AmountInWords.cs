using System;
using System.Collections.Generic;

namespace DanCI.BOQ.Core.Pricing
{
    /// <summary>
    /// Montant en toutes lettres, en francais (usage RDC / France : soixante-dix, quatre-vingt-dix),
    /// pour la formule « Arrêté le présent devis à la somme de … ». Regles : « et » pour 21…71,
    /// « quatre-vingts » et « cents » au pluriel seulement en fin de nombre, « mille » invariable,
    /// « million(s) » et « milliard(s) » accordes.
    /// </summary>
    public static class AmountInWords
    {
        private static readonly string[] Units =
        {
            "zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix",
            "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf"
        };

        private static readonly string[] Tens = { "", "dix", "vingt", "trente", "quarante", "cinquante", "soixante", "soixante", "quatre-vingt", "quatre-vingt" };

        /// <summary>« neuf mille sept cent neuf dollars américains et onze cents ».</summary>
        public static string Format(decimal amount, Currency currency)
        {
            if (amount < 0m) throw new ArgumentOutOfRangeException(nameof(amount), "Montant négatif.");
            decimal rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
            long whole = (long)Math.Floor(rounded);
            int cents = (int)Math.Round((rounded - whole) * 100m);
            (string one, string many, string subOne, string subMany) = Names(currency);

            string main = Number(whole);
            // « un million de dollars », « deux milliards de francs » : « de » apres million(s)/milliard(s) ronds.
            string link = whole > 0 && (main.EndsWith("million", StringComparison.Ordinal) || main.EndsWith("millions", StringComparison.Ordinal) ||
                                        main.EndsWith("milliard", StringComparison.Ordinal) || main.EndsWith("milliards", StringComparison.Ordinal))
                ? " de " : " ";
            string text = main + link + (whole > 1 ? many : one);
            if (cents > 0)
            {
                text += " et " + Number(cents) + " " + (cents > 1 ? subMany : subOne);
            }
            return text;
        }

        /// <summary>Phrase complete pour le pied de devis.</summary>
        public static string Sentence(decimal amount, Currency currency) =>
            "Arrêté le présent devis à la somme de : " + Capitalize(Format(amount, currency)) + " toutes taxes comprises.";

        private static (string, string, string, string) Names(Currency c) => c == Currency.CDF
            ? ("franc congolais", "francs congolais", "centime", "centimes")
            : ("dollar américain", "dollars américains", "cent", "cents");

        /// <summary>Entier positif en lettres.</summary>
        public static string Number(long n)
        {
            if (n == 0) return "zéro";
            var parts = new List<string>();
            long billions = n / 1_000_000_000;
            long millions = n / 1_000_000 % 1000;
            long thousands = n / 1000 % 1000;
            long rest = n % 1000;
            if (billions > 0) parts.Add(Below1000((int)billions, final: false) + (billions > 1 ? " milliards" : " milliard"));
            if (millions > 0) parts.Add(Below1000((int)millions, final: false) + (millions > 1 ? " millions" : " million"));
            if (thousands > 0) parts.Add(thousands == 1 ? "mille" : Below1000((int)thousands, final: false) + " mille");
            if (rest > 0) parts.Add(Below1000((int)rest, final: true));
            return string.Join(" ", parts);
        }

        /// <param name="final">Vrai si rien ne suit (accord de « cents » et « quatre-vingts »).</param>
        private static string Below1000(int n, bool final)
        {
            int hundreds = n / 100;
            int rest = n % 100;
            string text = string.Empty;
            if (hundreds > 0)
            {
                text = hundreds == 1 ? "cent" : Units[hundreds] + " cent";
                if (hundreds > 1 && rest == 0 && final) text += "s";
            }
            if (rest > 0)
            {
                text += (text.Length > 0 ? " " : string.Empty) + Below100(rest, final);
            }
            return text;
        }

        private static string Below100(int n, bool final)
        {
            if (n < 20) return Units[n];
            int ten = n / 10;
            int unit = n % 10;
            if (ten == 7 || ten == 9)
            {
                // 70-79 : soixante-dix… ; 90-99 : quatre-vingt-dix…
                int sub = 10 + unit;
                return Tens[ten] + (ten == 7 && unit == 1 ? " et " : "-") + Units[sub];
            }
            if (unit == 0)
            {
                return ten == 8 && final ? "quatre-vingts" : Tens[ten];
            }
            if (unit == 1 && ten != 8)
            {
                return Tens[ten] + " et un";
            }
            return Tens[ten] + "-" + Units[unit];
        }

        private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
