using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace WalletWasabi.Helpers
{
	// These helpers are implemented at a system level in .NET Core and .NET Standard 2.1
	// but not .NET Standard 2.0
	public static class FileAsyncHelpers
	{
		// source:
		// https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/async/using-async-for-file-access#parallel-asynchronous-io

		public static Task<string> ReadAllTextAsync(string path)
		{
			return ReadAllTextAsync(path, Encoding.UTF8);
		}

		public static async Task<string> ReadAllTextAsync(string path, Encoding encoding)
		{
			using (FileStream sourceStream = new FileStream(
						path,
						FileMode.Open,
						FileAccess.Read,
						FileShare.Read,
						bufferSize: 4096,
						useAsync: true))
			{
				StringBuilder sb = new StringBuilder();

				byte[] buffer = new byte[0x1000];
				int numRead;
				while ((numRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
				{
					string text = encoding.GetString(buffer, 0, numRead);
					sb.Append(text);
				}

				return sb.ToString();
			}
		}

		public static Task<string[]> ReadAllLinesAsync(string path)
		{
			return ReadAllLinesAsync(path, Encoding.UTF8);
		}

		// https://stackoverflow.com/a/13168006
		public static async Task<string[]> ReadAllLinesAsync(string path, Encoding encoding)
		{
			var lines = new List<string>();

			using (FileStream sourceStream = new FileStream(
						path,
						FileMode.Open,
						FileAccess.Read,
						FileShare.Read,
						bufferSize: 4096,
						useAsync: true))
			using (var reader = new StreamReader(sourceStream, encoding))
			{
				string line;
				while ((line = await reader.ReadLineAsync()) != null)
				{
					lines.Add(line);
				}
			}

			return lines.ToArray();
		}

		public static async Task<byte[]> ReadAllBytesAsync(string path)
		{
			using (FileStream sourceStream = new FileStream(
						path,
						FileMode.Open,
						FileAccess.Read,
						FileShare.Read,
						bufferSize: 4096,
						useAsync: true))
			{
				MemoryStream ms = new MemoryStream();

				byte[] buffer = new byte[0x1000];
				int bytesRead;
				while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
				{
					ms.Write(buffer, 0, bytesRead);
				}

				return ms.ToArray();
			}
		}

		public static Task WriteAllTextAsync(string path, string contents)
		{
			return WriteAllTextAsync(path, contents, Encoding.UTF8);
		}

		public static async Task WriteAllTextAsync(string path, string contents, Encoding encoding)
		{
			byte[] encodedText = encoding.GetBytes(contents);

			using (FileStream sourceStream = new FileStream(
				path,
				FileMode.Create,
				FileAccess.Write,
				FileShare.None,
				bufferSize: 4096,
				useAsync: true))
			{
				await sourceStream.WriteAsync(encodedText, 0, encodedText.Length);
			};
		}

		public static Task WriteAllBytesAsync(string path, byte[] bytes)
		{
			using (FileStream sourceStream = new FileStream(
				path,
				FileMode.Create,
				FileAccess.Write,
				FileShare.None,
				bufferSize: 4096,
				useAsync: true))
			{
				return sourceStream.WriteAsync(bytes, 0, bytes.Length);
			};
		}

		public static Task WriteAllLinesAsync(string path, IEnumerable<string> contents)
		{
			return WriteAllLinesAsync(path, contents, Encoding.UTF8);
		}

		public static async Task WriteAllLinesAsync(string path, IEnumerable<string> contents, Encoding encoding)
		{
			byte[] encodedText = encoding.GetBytes(string.Join(Environment.NewLine, contents));

			using (FileStream sourceStream = new FileStream(
				path,
				FileMode.Create,
				FileAccess.Write,
				FileShare.None,
				bufferSize: 4096,
				useAsync: true))
			{
				await sourceStream.WriteAsync(encodedText, 0, encodedText.Length);
			};
		}

		public static Task AppendAllLinesAsync(string path, IEnumerable<string> contents)
		{
			return AppendAllLinesAsync(path, contents, Encoding.UTF8);
		}

		public static async Task AppendAllLinesAsync(string path, IEnumerable<string> contents, Encoding encoding)
		{
			byte[] encodedText = encoding.GetBytes(string.Join(Environment.NewLine, contents));

			using (FileStream sourceStream = new FileStream(
				path,
				FileMode.Append,
				FileAccess.Write,
				FileShare.None,
				bufferSize: 4096,
				useAsync: true))
			{
				await sourceStream.WriteAsync(encodedText, 0, encodedText.Length);
			};
		}

	}
}
