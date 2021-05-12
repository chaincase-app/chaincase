using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Foundation;
using Chaincase.Common.Contracts;
using WalletWasabi.Logging;

namespace Chaincase.iOS.Services
{
    // based on https://github.com/brunobar79/react-native-cloud-fs/blob/master/ios/RNCloudFs.m
    // more help here https://docs.microsoft.com/en-us/xamarin/ios/data-cloud/introduction-to-icloud#document-storage

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "iCloud is iCloud")]
    public class iCloudService : IiCloudService
    {
        public static NSUrl iCloudDirectory =>
            NSFileManager.DefaultManager.GetUrlForUbiquityContainer(null);

        public static NSFileManager FileManager => NSFileManager.DefaultManager;

        public void MoveToCloud(string sourceFile, string destinationPath)
        {
            var ubiquityPath = iCloudDirectory?.Path;
            if (ubiquityPath != null)
            {
                var targetFile = Path.Combine(ubiquityPath, destinationPath);
                var dir = Directory.GetParent(targetFile).FullName;
                var name = Path.GetFileName(targetFile);

                var uniqueFile = targetFile;
                int count = 1;
                while (FileManager.FileExists(uniqueFile))
                {
                    var uniqueName = uniqueFile + count;
                    uniqueFile = Path.Combine(dir, uniqueName);
                    count++;
                }

                if (!FileManager.FileExists(uniqueFile))
                {
                    FileManager.CreateDirectory(dir, createIntermediates: true, null);
                }

                NSFileManager.DefaultManager.SetUbiquitous(true, new(sourceFile), new(uniqueFile), out NSError error);
                if (error != null)
                {
                    throw new Exception(error.Description);
                }
            }
            else
            {
                Console.WriteLine("Can't find iCloud container, check your provisioning profile and entitlements");
            }
        }

        public void FetchCloudDocument(string filename, string localPath)
        {

            Console.WriteLine("FindDocument");
            var query = new NSMetadataQuery
            {
                SearchScopes = new NSObject[] { NSMetadataQuery.UbiquitousDocumentsScope }
            };

            var pred = NSPredicate.FromFormat("%K == %@", new NSObject[] {
                NSMetadataQuery.ItemFSNameKey, new NSString(filename)
            });
            Console.WriteLine("Predicate:{0}", pred.PredicateFormat);
            query.Predicate = pred;


            NSNotificationCenter.DefaultCenter.AddObserver(
                NSMetadataQuery.DidFinishGatheringNotification,
                obj: query,
                queue: NSOperationQueue.CurrentQueue,
                (NSNotification notification) =>
                {
                    var query = (NSMetadataQuery)notification.Object;
                    query.DisableUpdates();
                    query.StopQuery();
                    foreach (var item in query.Results)
                    {
                        if (item.ValueForAttribute(NSMetadataQuery.ItemFSNameKey)
                                .IsEqual(new NSString(filename)))
                        {
                            var url = (NSUrl)item.ValueForAttribute(NSMetadataQuery.ItemURLKey);
                            bool isFileReady = DownloadFileIfNotAvailable(item);
                            if (isFileReady)
                            {
                                var data = NSData.FromUrl(url);
                                data.Save(new NSUrl(localPath), atomically: true);
                            }
                            else
                            {
                                // retry recursively until the file is ready
                                FetchCloudDocument(filename, localPath);
                            }
                        }
                    }
                }
            );


            query.StartQuery();
        }

        private static bool DownloadFileIfNotAvailable(NSMetadataItem item)
        {
            if (item.ValueForAttribute(NSMetadataQuery.UbiquitousItemDownloadingStatusKey)
                .IsEqual(new NSString("NSMetadataUbiquitousItemDownloadingStatusCurrent")))
            {
                return true;
            }

            FileManager.StartDownloadingUbiquitous((NSUrl)item.ValueForAttribute(NSMetadataQuery.ItemURLKey), out NSError error);
            if (error != null)
            {
                Logger.LogError($"Error occurred starting download: {error.Description}");
            }
            Logger.LogInfo("Idling before retrying...");
            Thread.Sleep(300);
            return false;
        }

    }
}
