using System.Threading.Tasks;

namespace Chaincase.Common.Contracts
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "iCloud is iCloud")]
    public interface IiCloudService
    {
        void MoveToCloud(string sourceFile, string destinationPath);

        void FetchCloudDocument(string filename, string localPath);
    }
}
