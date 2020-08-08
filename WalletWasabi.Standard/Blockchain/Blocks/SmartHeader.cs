using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WalletWasabi.Exceptions;
using WalletWasabi.Helpers;

namespace WalletWasabi.Blockchain.Blocks
{
	public class SmartHeader
	{
		public SmartHeader(uint256 blockHash, uint256 prevHash, uint height, DateTimeOffset blockTime)
		{
			BlockHash = Guard.NotNull(nameof(blockHash), blockHash);
			PrevHash = Guard.NotNull(nameof(prevHash), prevHash);
			if (blockHash == prevHash)
			{
				throw new InvalidOperationException($"{nameof(blockHash)} cannot be equal to {nameof(prevHash)}. Value: {blockHash}.");
			}

			Height = height;
			BlockTime = blockTime;
		}

		public uint256 BlockHash { get; }
		public uint256 PrevHash { get; }
		public uint Height { get; }
		public DateTimeOffset BlockTime { get; }

		#region SpecialHeaders

		private static SmartHeader StartingHeaderMain { get; } = new SmartHeader(
			new uint256("0000000000000000000fe253089b74d682de036b97a05aa777d047013c413969"),
			new uint256("000000000000000000088dd695cb0747cac74ae6962f74ce1270300e3ec9e896"),
			634764,
			DateTimeOffset.FromUnixTimeSeconds(1592168005));

		private static SmartHeader StartingHeaderTestNet { get; } = new SmartHeader(
			new uint256("00000000000f0d5edcaeba823db17f366be49a80d91d15b77747c2e017b8c20a"),
			new uint256("0000000000211a4d54bceb763ea690a4171a734c48d36f7d8e30b51d6df6ea85"),
			828575,
			DateTimeOffset.FromUnixTimeSeconds(1463079943));

		private static SmartHeader StartingHeaderRegTest { get; } = new SmartHeader(
			Network.RegTest.GenesisHash,
			Network.RegTest.GetGenesis().Header.HashPrevBlock,
			0,
			Network.RegTest.GetGenesis().Header.BlockTime);

		/// <summary>
		/// Where the first possible bech32 transaction ever can be found.
		/// </summary>
		public static SmartHeader GetStartingHeader(Network network)
			=> network.NetworkType switch
			{
				NetworkType.Mainnet => StartingHeaderMain,
				NetworkType.Testnet => StartingHeaderTestNet,
				NetworkType.Regtest => StartingHeaderRegTest,
				_ => throw new NotSupportedNetworkException(network)
			};

		#endregion SpecialHeaders
	}
}
