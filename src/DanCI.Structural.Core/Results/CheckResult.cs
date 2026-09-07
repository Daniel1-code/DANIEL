using System.Collections.Generic;

namespace DanCI.Structural.Core.Results
{
    public enum CheckStatus
    {
        NotApplicable,
        Pass,
        Warning,
        Fail
    }

    /// <summary>
    /// Resultat tracable d'une verification reglementaire. Chaque vérification du moteur en
    /// renvoie un : la note de calcul, le tableau de resultats, le tableau de bord et les
    /// couleurs de controle ne sont que quatre mises en forme de cette meme liste.
    /// </summary>
    public sealed class CheckResult
    {
        /// <summary>Norme appliquee, par exemple "EN 1992-1-1:2004".</summary>
        public string Code { get; set; }

        /// <summary>Article, par exemple "9.5.2 (2)".</summary>
        public string Clause { get; set; }

        /// <summary>Equation utilisee, en clair.</summary>
        public string Equation { get; set; }

        /// <summary>Ce qui est verifie, en une phrase.</summary>
        public string Description { get; set; }

        /// <summary>Valeurs d'entree ayant servi au calcul.</summary>
        public Dictionary<string, Quantity> Inputs { get; private set; }

        /// <summary>Sollicitation.</summary>
        public Quantity Demand { get; set; }

        /// <summary>Resistance.</summary>
        public Quantity Resistance { get; set; }

        /// <summary>Taux de travail : sollicitation / resistance.</summary>
        public double Utilization { get; set; }

        public CheckStatus Status { get; set; }

        /// <summary>Combinaison dimensionnante ayant conduit a ce resultat.</summary>
        public string GoverningCombination { get; set; }

        /// <summary>Commentaire libre (hypothese retenue, remarque).</summary>
        public string Comment { get; set; }

        public CheckResult()
        {
            Inputs = new Dictionary<string, Quantity>();
            Status = CheckStatus.NotApplicable;
        }

        public CheckResult WithInput(string name, Quantity value)
        {
            Inputs[name] = value;
            return this;
        }

        /// <summary>Renseigne demande, resistance, taux et statut en une fois.</summary>
        public CheckResult Verify(Quantity demand, Quantity resistance)
        {
            Demand = demand;
            Resistance = resistance;
            Utilization = resistance.Value > 0 ? demand.Value / resistance.Value : double.PositiveInfinity;
            Status = Utilization <= 1.0 ? CheckStatus.Pass : CheckStatus.Fail;
            return this;
        }

        public string StatusLabel
        {
            get
            {
                switch (Status)
                {
                    case CheckStatus.Pass: return "OK";
                    case CheckStatus.Warning: return "A verifier";
                    case CheckStatus.Fail: return "NON CONFORME";
                    default: return "Non applicable";
                }
            }
        }
    }
}
