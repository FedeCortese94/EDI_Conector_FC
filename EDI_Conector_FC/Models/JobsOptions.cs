namespace EDI_Conector_FC.Models
{
    public class JobsOptions
    {
        // Jobs legacy (ECI directo a SAP)
        public bool JobOrdersIn { get; set; }
        public bool JobDesadvOut { get; set; }
        public bool JobInvoicesOut { get; set; }

        // Jobs nuevos (multi-cliente via CSV)
        public bool JobOrdersEdiToCsv { get; set; }
        public bool JobOrdersCsvToSap { get; set; }
    }
}
