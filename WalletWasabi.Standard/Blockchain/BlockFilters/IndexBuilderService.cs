using NBitcoin;
using NBitcoin.Protocol;
using System;

namespace WalletWasabi.Blockchain.BlockFilters
{
	public class IndexBuilderService
	{
		public static byte[][] DummyScript { get; } = new byte[][] { ByteHelpers.FromHex("0009BBE4C2D17185643765C265819BF5261755247D") };

		public static GolombRiceFilter CreateDummyEmptyFilter(uint256 blockHash)
		{
			return new GolombRiceFilterBuilder()
				.SetKey(blockHash)
				.SetP(20)
				.SetM(1 << 20)
				.AddEntries(DummyScript)
				.Build();
		}
	}
}
