using Autodesk.Revit.ApplicationServices;
using DanCI.BOQ.Core.Diagnostics;

namespace DanCI.BOQ.RevitAdapter
{
    /// <summary>Lit l'identite de Revit (version, build, langue) pour le diagnostic.</summary>
    public static class RevitHostReader
    {
        /// <summary>Au demarrage (IExternalApplication.OnStartup).</summary>
        public static HostInfo Read(ControlledApplication application)
        {
            return new HostInfo
            {
                VersionNumber = application.VersionNumber,
                SubVersionNumber = application.SubVersionNumber,
                VersionBuild = application.VersionBuild,
                VersionName = application.VersionName,
                Language = application.Language.ToString()
            };
        }

        /// <summary>Depuis une commande (ExternalCommandData.Application.Application).</summary>
        public static HostInfo Read(Application application)
        {
            return new HostInfo
            {
                VersionNumber = application.VersionNumber,
                SubVersionNumber = application.SubVersionNumber,
                VersionBuild = application.VersionBuild,
                VersionName = application.VersionName,
                Language = application.Language.ToString()
            };
        }
    }
}
