namespace DanCI.BOQ.Core.Model
{
    /// <summary>
    /// Identite du projet pour la page de garde et le devis. Lue dans les Informations sur le
    /// projet de Revit, puis completee ou remplacee par les parametres du projet saisis dans
    /// l'extension (voir ProjectDetails).
    /// </summary>
    public sealed class ProjectInfo
    {
        public string Name { get; set; }
        public string Number { get; set; }
        public string Client { get; set; }
        public string Location { get; set; }
        public string Author { get; set; }
        public string Revision { get; set; }
        public string IssueDate { get; set; }
        public string Status { get; set; }

        /// <summary>Objet du devis (ex. « Construction d'un immeuble R+3 a usage de bureaux »).</summary>
        public string Purpose { get; set; }
        public string Architect { get; set; }
        public string Engineer { get; set; }
        public string Contractor { get; set; }
        public string QuoteReference { get; set; }
        public string CheckedBy { get; set; }
        public string ValidityDays { get; set; }
        public string ExecutionTime { get; set; }
        public string PaymentTerms { get; set; }
        public string Notes { get; set; }

        /// <summary>Surface de reference (m²) pour le cout au m² du tableau de bord ; null = non renseignee.</summary>
        public decimal? ReferenceArea { get; set; }
        public string ReferenceAreaLabel { get; set; }
    }
}
