namespace EDI_Conector_FC.Models.ClientConfig
{
    /// <summary>
    /// Configuración completa de un cliente EDI.
    /// Cada cliente tiene su propio fichero JSON en Clients/{ClientId}/config.json
    /// </summary>
    public sealed class ClientOptions
    {
        public string ClientId { get; set; } = "";
        public string Description { get; set; } = "";
        public bool Enabled { get; set; } = true;

        public ClientFtpOptions Ftp { get; set; } = new();
        public ClientPathsOptions Paths { get; set; } = new();
        public ClientSapDefaults SapDefaults { get; set; } = new();
        public ClientParserOptions Parser { get; set; } = new();
    }

    public sealed class ClientFtpOptions
    {
        /// <summary>Si false, se salta la descarga FTP y se usan los ficheros del Inbox directamente.</summary>
        public bool Enabled { get; set; } = true;
        public string Host { get; set; } = "";
        public int Port { get; set; } = 21;
        public string User { get; set; } = "";
        public string Password { get; set; } = "";
        public string RemoteFolderOrders { get; set; } = "";
        public string FilePrefix { get; set; } = "";
    }

    public sealed class ClientPathsOptions
    {
        public string OrdersInbox { get; set; } = "";
        public string OrdersCsv { get; set; } = "";
        public string OrdersProcessed { get; set; } = "";
        public string OrdersError { get; set; } = "";
    }

    public sealed class ClientSapDefaults
    {
        /// <summary>CardCode del cliente en SAP B1</summary>
        public string CardCode { get; set; } = "";

        /// <summary>Código de almacén por defecto</summary>
        public string WarehouseCode { get; set; } = "";

        /// <summary>Vendedor (SlpCode)</summary>
        public int SlpCode { get; set; } = 0;

        /// <summary>Divisa</summary>
        public string Currency { get; set; } = "EUR";

        /// <summary>Grupo de pago (GroupNumber)</summary>
        public int GroupNumber { get; set; } = 0;

        /// <summary>Método de pago (PaymentMethod)</summary>
        public string PaymentMethod { get; set; } = "";

        /// <summary>Código transportista</summary>
        public int Transportista { get; set; } = 0;

        /// <summary>Comentario base que se añade al pedido</summary>
        public string CommentsPrefix { get; set; } = "EDI";
    }

    public sealed class ClientParserOptions
    {
        /// <summary>
        /// Longitud objetivo del EAN. Si el EAN viene corto se rellena con ceros por la izquierda.
        /// Estándar: 13. Poner 0 para no rellenar.
        /// </summary>
        public int EanPadToLength { get; set; } = 13;
    }
}
