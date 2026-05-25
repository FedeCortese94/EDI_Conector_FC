using System;
using System.IO;

namespace EDI_Conector_FC.Utils.IO
{
	public static class DirectoryEnsurer
	{
		public static void Ensure(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return;

			if (!Directory.Exists(path))
			{
				Directory.CreateDirectory(path);
				Console.WriteLine($"[{DateTime.Now}] Carpeta creada: {path}");
			}
		}
	}
}
