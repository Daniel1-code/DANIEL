namespace DanCI.BOQ.Core.Model
{
    /// <summary>Identite du projet pour la page de garde (lue dans les Informations sur le projet de Revit).</summary>
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
    }
}
