using System.Collections.Generic;

namespace System.Linq
{
	public static class LinqExtensions
	{
		public static T RandomElement<T>(this IEnumerable<T> source)
		{
			T current = default;
			int count = 0;
			foreach (T element in source)
			{
				count++;
				if (new Random().Next(count) == 0)
				{
					current = element;
				}
			}
			if (count == 0)
			{
				return default;
			}
			return current;
		}

		public static void Shuffle<T>(this IList<T> list)
		{
			var rng = new Random();
			int n = list.Count;
			while (n > 1)
			{
				n--;
				int k = rng.Next(n + 1);
				T value = list[k];
				list[k] = list[n];
				list[n] = value;
			}
		}

		// https://stackoverflow.com/a/2992364
		public static void RemoveByValue<TKey, TValue>(this Dictionary<TKey, TValue> me, TValue value)
		{
			var itemsToRemove = new List<TKey>();

			foreach (var pair in me)
			{
				if (pair.Value.Equals(value))
				{
					itemsToRemove.Add(pair.Key);
				}
			}

			foreach (TKey item in itemsToRemove)
			{
				me.Remove(item);
			}
		}

		// https://stackoverflow.com/a/2992364
		public static void RemoveByValue<TKey, TValue>(this SortedDictionary<TKey, TValue> me, TValue value)
		{
			var itemsToRemove = new List<TKey>();

			foreach (var pair in me)
			{
				if (pair.Value.Equals(value))
				{
					itemsToRemove.Add(pair.Key);
				}
			}

			foreach (TKey item in itemsToRemove)
			{
				me.Remove(item);
			}
		}

		public static bool NotNullAndNotEmpty<T>(this IEnumerable<T> source)
		{
			return !(source is null) && source.Any();
		}

		public static IEnumerable<IEnumerable<T>> GetPermutations<T>(this IEnumerable<T> items, int count)
		{
			int i = 0;
			foreach (var item in items)
			{
				if (count == 1)
				{
					yield return new T[] { item };
				}
				else
				{
					foreach (var result in items.Skip(i + 1).GetPermutations(count - 1))
					{
						yield return new T[] { item }.Concat(result);
					}
				}

				++i;
			}
		}

		// https://stackoverflow.com/a/3471927
		public static HashSet<T> ToHashSet<T>(
			this IEnumerable<T> source,
			IEqualityComparer<T> comparer = null)
		{
			return new HashSet<T>(source, comparer);
		}

		// https://github.com/dotnet/corefx/issues/13842#issuecomment-261823388
		public static IEnumerable<T> TakeLast<T>(this IEnumerable<T> source, int count)
		{
			var buffer = new List<T>();
			int pos = 0;

			foreach (var item in source)
			{
				if (buffer.Count < count)
				{
					// phase 1
					buffer.Add(item);
				}
				else
				{
					// phase 2
					buffer[pos] = item;
					pos = (pos + 1) % count;
				}
			}

			for (int i = 0; i < buffer.Count; i++)
			{
				yield return buffer[pos];
				pos = (pos + 1) % count;
			}
		}

		public static IEnumerable<T> SkipLast<T>(this IEnumerable<T> source, int count)
		{
			var buffer = new List<T>();
			int pos = 0;

			foreach (var item in source)
			{
				if (buffer.Count < count)
				{
					// phase 1
					buffer.Add(item);
				}
				else
				{
					// phase 2
					yield return buffer[pos];
					buffer[pos] = item;
					pos = (pos + 1) % count;
				}
			}
		}
	}
}
