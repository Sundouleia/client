namespace Sundouleia.Utils;

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

/// <summary>
///   A utility class to deduplicate tasks based on a unique key, allowing multiple callers to await the same task instance.
/// </summary>
/// <typeparam name="TKey"></typeparam>
public class TaskDeduplicator<TKey> where TKey : notnull
{

	private readonly ConcurrentDictionary<TKey, Task> _tasks = new();

	/// <summary>
	/// Gets a running task for the given key if it already exists, or creates and starts a new one.
	/// All callers for the same key will await the same underlying task.
	/// </summary>
	/// <param name="key">A unique key to identify the operation, e.g. file hash.</param>
	/// <param name="taskFactory">A function that returns the long-running Task to be executed, e.g. task wrapping the download + saving to cache.</param>
	/// <returns>A Task that represents the completion of the work.</returns>
	public Task<T> GetOrBeginTask<T>(TKey key, Func<Task<T>> taskFactory)
	{
		var task = _tasks.GetOrAdd(key, k => Task.Run(taskFactory).ContinueWith(t =>
		{
			// Remove the task from the dictionary once it's complete
			_tasks.TryRemove(k, out _);
			return t.Result;
		}));
		return (Task<T>)task;
	}

	/// <summary>
	/// Void version of the function. See <see cref="GetOrBeginTask{T}(TKey, Func{Task{T}})"/> for details.
	/// </summary>
	public Task GetOrBeginTask(TKey key, Func<Task> taskFactory)
	{
		var task = _tasks.GetOrAdd(key, k => Task.Run(taskFactory).ContinueWith(t =>
		{
			// Remove the task from the dictionary once it's complete
			_tasks.TryRemove(k, out _);
			return t;
		}));
		return task;
	}

	/// <summary>
	/// Tries to get an existing task for the given key.
	/// </summary>
	/// <param name="key">The unique key identifying the operation.</param>
	/// <param name="task">The existing task if found; otherwise, null.</param>
	/// <returns>True if a task was found for the key; otherwise, false.</returns>
	public bool TryGetTask(TKey key, out Task task)
	{
		return _tasks.TryGetValue(key, out task!);
	}
}
