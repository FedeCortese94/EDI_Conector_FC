namespace EDI_Conector_FC.Models
{
	public class PacksOptions
	{
		/// <summary>
		/// Si es true, las líneas que pertenecen al mismo pack (@INTRX_KT_PACK) se unifican
		/// en una sola línea de salida en DESADV/INVOIC. Si es false o la clave no está
		/// presente en appsettings.json, se envían siempre las tallas sueltas (comportamiento
		/// por defecto).
		/// </summary>
		public bool EnviarPacksUnificados { get; set; } = false;
	}
}
