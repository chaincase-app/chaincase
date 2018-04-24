﻿using NBitcoin;
using NBitcoin.RPC;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.ChaumianCoinJoin;
using WalletWasabi.Crypto;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;

namespace WalletWasabi.Services
{
	public class CcjCoordinator : IDisposable
	{
		private List<CcjRound> Rounds { get; }
		private AsyncLock RoundsListLock { get; }

		private List<uint256> UnconfirmedCoinJoins { get; }
		private List<uint256> CoinJoins { get; }
		public string CoinJoinsFilePath => Path.Combine(FolderPath, $"CoinJoins{Network}.txt");
		private AsyncLock CoinJoinsLock { get; }

		public RPCClient RpcClient { get; }

		public CcjRoundConfig RoundConfig { get; private set; }

		public Network Network { get; }

		public string FolderPath { get; }
		
		public BlindingRsaKey RsaKey { get; }

		public CcjCoordinator(Network network, string folderPath, RPCClient rpc, CcjRoundConfig roundConfig)
		{
			Network = Guard.NotNull(nameof(network), network);
			FolderPath = Guard.NotNullOrEmptyOrWhitespace(nameof(folderPath), folderPath, trim: true);
			RpcClient = Guard.NotNull(nameof(rpc), rpc);
			RoundConfig = Guard.NotNull(nameof(roundConfig), roundConfig);

			Rounds = new List<CcjRound>();
			RoundsListLock = new AsyncLock();

			CoinJoins = new List<uint256>();
			UnconfirmedCoinJoins = new List<uint256>();
			CoinJoinsLock = new AsyncLock();

			Directory.CreateDirectory(FolderPath);

			// Initialize RsaKey
			string rsaKeyPath = Path.Combine(FolderPath, "RsaKey.json");
			if (File.Exists(rsaKeyPath))
			{
				string rsaKeyJson = File.ReadAllText(rsaKeyPath, encoding: Encoding.UTF8);
				RsaKey = BlindingRsaKey.CreateFromJson(rsaKeyJson);
			}
			else
			{
				RsaKey = new BlindingRsaKey();
				File.WriteAllText(rsaKeyPath, RsaKey.ToJson(), encoding: Encoding.UTF8);
				Logger.LogInfo<CcjCoordinator>($"Created RSA key at: {rsaKeyPath}");
			}

			if(File.Exists(CoinJoinsFilePath))
			{
				try
				{
					var toRemove = new List<string>();
					string[] allLines = File.ReadAllLines(CoinJoinsFilePath);
					foreach (string line in allLines)
					{
						uint256 txHash = new uint256(line);
						RPCResponse getRawTransactionResponse = RpcClient.SendCommand(RPCOperations.getrawtransaction, txHash.ToString(), true);
						if (string.IsNullOrWhiteSpace(getRawTransactionResponse?.ResultString))
						{
							toRemove.Add(line);
						}
						else
						{
							CoinJoins.Add(txHash);
							if (getRawTransactionResponse.Result.Value<int>("confirmations") <= 0)
							{
								UnconfirmedCoinJoins.Add(txHash);
							}
						}
					}

					if (toRemove.Count != 0) // a little performance boost, it'll be empty almost always
					{
						var newAllLines = allLines.Where(x => !toRemove.Contains(x));
						File.WriteAllLines(CoinJoinsFilePath, newAllLines);
					}
				}
				catch (Exception ex)
				{
					Logger.LogWarning<CcjCoordinator>($"CoinJoins file got corrupted. Deleting {CoinJoinsFilePath}. {ex.GetType()}: {ex.Message}");
					File.Delete(CoinJoinsFilePath);
				}
			}
		}

		public void UpdateRoundConfig(CcjRoundConfig roundConfig)
		{
			RoundConfig = Guard.NotNull(nameof(roundConfig), roundConfig);
		}

		public async Task MakeSureTwoRunningRoundsAsync()
		{
			using (await RoundsListLock.LockAsync())
			{
				int runningRoundCount = Rounds.Count(x => x.Status == CcjRoundStatus.Running);
				if (runningRoundCount == 0)
				{
					var round = new CcjRound(RpcClient, RoundConfig);
					round.StatusChanged += Round_StatusChangedAsync;
					await round.ExecuteNextPhaseAsync(CcjRoundPhase.InputRegistration);
					Rounds.Add(round);

					var round2 = new CcjRound(RpcClient, RoundConfig);
					round2.StatusChanged += Round_StatusChangedAsync;
					await round2.ExecuteNextPhaseAsync(CcjRoundPhase.InputRegistration);
					Rounds.Add(round2);
				}
				else if(runningRoundCount == 1)
				{
					var round = new CcjRound(RpcClient, RoundConfig);
					round.StatusChanged += Round_StatusChangedAsync;
					await round.ExecuteNextPhaseAsync(CcjRoundPhase.InputRegistration);
					Rounds.Add(round);
				}
			}
		}

		private async void Round_StatusChangedAsync(object sender, CcjRoundStatus status)
		{
			var round = sender as CcjRound;

			// If success save the coinjoin.
			if (status == CcjRoundStatus.Succeded)
			{
				using (await CoinJoinsLock.LockAsync())
				{
					uint256 coinJoinHash = round.SignedCoinJoin.GetHash();
					CoinJoins.Add(coinJoinHash);
					await File.AppendAllLinesAsync(CoinJoinsFilePath, new[] { coinJoinHash.ToString() });
				}
			}

			// If finished start a new round.
			if (status == CcjRoundStatus.Failed || status == CcjRoundStatus.Succeded)
			{
				round.StatusChanged -= Round_StatusChangedAsync;
				await MakeSureTwoRunningRoundsAsync();
			}
		}

		public void FailAllRoundsInInputRegistration()
		{
			using (RoundsListLock.Lock())
			{
				foreach (var r in Rounds.Where(x => x.Status == CcjRoundStatus.Running && x.Phase == CcjRoundPhase.InputRegistration))
				{
					r.Fail();
				}
			}
		}

		public void FailAllRunningRounds()
		{
			using (RoundsListLock.Lock())
			{
				foreach (var r in Rounds.Where(x => x.Status == CcjRoundStatus.Running))
				{
					r.Fail();
				}
			}
		}

		public CcjRound GetLastSuccessfulRound()
		{
			using (RoundsListLock.Lock())
			{
				return Rounds.LastOrDefault(x => x.Status == CcjRoundStatus.Succeded);
			}
		}

		public CcjRound GetLastFailedRound()
		{
			using (RoundsListLock.Lock())
			{
				return Rounds.LastOrDefault(x => x.Status == CcjRoundStatus.Failed);
			}
		}

		public CcjRound GetLastRound()
		{
			using (RoundsListLock.Lock())
			{
				return Rounds.LastOrDefault(x => x.Status != CcjRoundStatus.Running);
			}
		}

		public CcjRound GetCurrentRound()
		{
			using (RoundsListLock.Lock())
			{
				return Rounds.First(x => x.Status == CcjRoundStatus.Running); // not FirstOrDefault, it must always exist
			}
		}

		public CcjRound GetCurrentInputRegisterableRound()
		{
			using (RoundsListLock.Lock())
			{
				return Rounds.First(x => x.Status == CcjRoundStatus.Running && x.Phase == CcjRoundPhase.InputRegistration); // not FirstOrDefault, it must always exist
			}
		}

		public CcjRound GetNextRound()
		{
			using (RoundsListLock.Lock())
			{
				return Rounds.LastOrDefault(x => x.Status == CcjRoundStatus.Running);
			}
		}

		public bool AnyRunningRoundContainsInput(OutPoint input, out List<Alice> alices)
		{
			using (RoundsListLock.Lock())
			{
				alices = new List<Alice>();
				foreach(var round in Rounds.Where(x=>x.Status == CcjRoundStatus.Running))
				{
					if(round.ContainsInput(input, out List<Alice> roundAlices))
					{
						foreach(var alice in roundAlices)
						{
							alices.Add(alice);
						}
					}
				}
				return alices.Count > 0;
			}
		}

		public bool ContainsCoinJoin(uint256 hash)
		{
			using (CoinJoinsLock.Lock())
			{
				return CoinJoins.Contains(hash);
			}
		}

		public async Task<bool> IsUnconfirmedCoinJoinLimitReachedAsync()
		{
			using (await CoinJoinsLock.LockAsync())
			{
				if(UnconfirmedCoinJoins.Count() < 24)
				{
					return false;
				}
				else
				{
					foreach(var cjHash in UnconfirmedCoinJoins)
					{
						RPCResponse getRawTransactionResponse = await RpcClient.SendCommandAsync(RPCOperations.getrawtransaction, cjHash.ToString(), true);
						// if failed remove from everywhere (should not happen normally)
						if (string.IsNullOrWhiteSpace(getRawTransactionResponse?.ResultString))
						{
							UnconfirmedCoinJoins.Remove(cjHash);
							CoinJoins.Remove(cjHash);
							await File.WriteAllLinesAsync(CoinJoinsFilePath, CoinJoins.Select(x=>x.ToString()));
						}
						// if confirmed remove only from unconfirmed
						if(getRawTransactionResponse.Result.Value<int>("confirmations") > 0)
						{
							UnconfirmedCoinJoins.Remove(cjHash);
						}
					}
				}

				return UnconfirmedCoinJoins.Count() >= 24;
			}
		}

		#region IDisposable Support

		private volatile bool _disposedValue = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				if (disposing)
				{
					using (RoundsListLock.Lock())
					{
						foreach(CcjRound round in Rounds)
						{
							round.StatusChanged -= Round_StatusChangedAsync;
						}
					}
				}

				_disposedValue = true;
			}
		}

		// ~CcjCoordinator() {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		// This code added to correctly implement the disposable pattern.
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// GC.SuppressFinalize(this);
		}
		#endregion
	}
}
