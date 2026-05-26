using EDI_Conector_FC.Models;
using EDI_Conector_FC.Models.ClientConfig;
using FluentFTP;
using Microsoft.Extensions.Logging;
using System.Net;

namespace EDI_Conector_FC.Services.Remote
{
    /// <summary>
    /// Factoría que crea un IFtpService a partir de la configuración de un cliente.
    /// Permite que cada cliente tenga sus propias credenciales FTP sin tocar appsettings.
    /// </summary>
    public interface IFtpServiceFactory
    {
        IFtpService Create(ClientFtpOptions ftpOpts);
    }

    public sealed class FtpServiceFactory : IFtpServiceFactory
    {
        private readonly ILoggerFactory _loggerFactory;

        public FtpServiceFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public IFtpService Create(ClientFtpOptions ftpOpts)
        {
            // Convertimos ClientFtpOptions → FtpOptions (el modelo que ya usa FtpService)
            var opts = new FtpOptions
            {
                Host                = ftpOpts.Host,
                Port                = ftpOpts.Port,
                User                = ftpOpts.User,
                Password            = ftpOpts.Password,
                RemoteFolderOrders  = ftpOpts.RemoteFolderOrders,
                FilePrefix          = ftpOpts.FilePrefix,
            };

            return new FtpService(
                _loggerFactory.CreateLogger<FtpService>(),
                Microsoft.Extensions.Options.Options.Create(opts));
        }
    }
}
