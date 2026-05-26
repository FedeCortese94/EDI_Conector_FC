namespace EDI_Conector_FC.Models
{
    public class QuartzScheduleOptions
    {
        // Legacy
        public string JobOrdersIn { get; set; } = "0 */1 * ? * *";
        public string JobDesadvOut { get; set; } = "0 */5 * ? * *";
        public string JobInvoicesOut { get; set; } = "0 */2 * ? * *";

        // Nuevos
        public string JobOrdersEdiToCsv { get; set; } = "0 */5 * ? * *";
        public string JobOrdersCsvToSap { get; set; } = "0 */6 * ? * *";
    }
}
