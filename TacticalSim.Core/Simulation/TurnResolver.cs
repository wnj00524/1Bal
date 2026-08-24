using System;
using System.Collections.Generic;
using System.Linq;
using TacticalSim.Core.World;

namespace TacticalSim.Core.Simulation
{
    /// <summary>
    /// Simultaneous turn resolution engine managing a global timeline,
    /// concurrent multi-entity scheduling, per-actor queues, and fractionated TU advancement.
    /// </summary>
    public class TurnResolver : ITurnResolver
    {
        private const float Epsilon = 1e-5f;

        private float _globalTime = 0.0f;
        private readonly Dictionary<Guid, TacticalAction> _activeActions = new();
        private readonly Dictionary<Guid, Queue<TacticalAction>> _actorQueues = new();
        private readonly ITacticalWorld _world;

        public TurnResolver(ITacticalWorld world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <inheritdoc />
        public float GlobalTime => _globalTime;

        /// <inheritdoc />
        public bool HasActiveActions
        {
            get
            {
                if (_activeActions.Count > 0)
                {
                    return true;
                }

                foreach (var queue in _actorQueues.Values)
                {
                    if (queue.Count > 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <inheritdoc />
        public int ActiveActorCount => _activeActions.Count;

        /// <inheritdoc />
        public event EventHandler<ActionEventArgs>? ActionScheduled;

        /// <inheritdoc />
        public event EventHandler<ActionEventArgs>? ActionStarted;

        /// <inheritdoc />
        public event EventHandler<ActionProgressEventArgs>? ActionProgressed;

        /// <inheritdoc />
        public event EventHandler<ActionEventArgs>? ActionCompleted;

        /// <inheritdoc />
        public event EventHandler<ActionEventArgs>? ActionCancelled;

        /// <inheritdoc />
        public event EventHandler<ActionFailedEventArgs>? ActionFailed;

        /// <inheritdoc />
        public event EventHandler<TimeAdvancedEventArgs>? TimeAdvanced;

        /// <inheritdoc />
        public void ScheduleAction(TacticalAction action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (action.ActorId == Guid.Empty)
            {
                throw new ArgumentException("ActorId cannot be empty.", nameof(action));
            }

            if (action.TUCost <= 0f || float.IsNaN(action.TUCost) || float.IsInfinity(action.TUCost))
            {
                throw new ArgumentException("TUCost must be strictly positive and finite.", nameof(action));
            }

            if (action.State != TacticalActionState.Pending)
            {
                throw new InvalidOperationException($"Cannot schedule action with state '{action.State}'. Action state must be Pending.");
            }

            if (!_activeActions.ContainsKey(action.ActorId))
            {
                _activeActions[action.ActorId] = action;
            }
            else
            {
                if (!_actorQueues.TryGetValue(action.ActorId, out var queue))
                {
                    queue = new Queue<TacticalAction>();
                    _actorQueues[action.ActorId] = queue;
                }
                queue.Enqueue(action);
            }

            ActionScheduled?.Invoke(this, new ActionEventArgs(action, _globalTime));
        }

        /// <inheritdoc />
        public bool CancelAction(Guid actionId)
        {
            if (actionId == Guid.Empty)
            {
                return false;
            }

            // Check if action is currently active
            var activeKvp = _activeActions.FirstOrDefault(kvp => kvp.Value.Id == actionId);
            if (activeKvp.Value != null)
            {
                var actorId = activeKvp.Key;
                var action = activeKvp.Value;
                _activeActions.Remove(actorId);

                action.State = TacticalActionState.Cancelled;
                action.OnCancel();
                ActionCancelled?.Invoke(this, new ActionEventArgs(action, _globalTime));

                // Promote next queued action if available
                if (_actorQueues.TryGetValue(actorId, out var queue) && queue.Count > 0)
                {
                    var nextAction = queue.Dequeue();
                    _activeActions[actorId] = nextAction;
                    if (queue.Count == 0)
                    {
                        _actorQueues.Remove(actorId);
                    }
                }

                return true;
            }

            // Check queued actions
            foreach (var (actorId, queue) in _actorQueues.ToList())
            {
                if (queue.Any(a => a.Id == actionId))
                {
                    var newQueue = new Queue<TacticalAction>();
                    TacticalAction? foundAction = null;

                    while (queue.Count > 0)
                    {
                        var item = queue.Dequeue();
                        if (item.Id == actionId)
                        {
                            foundAction = item;
                        }
                        else
                        {
                            newQueue.Enqueue(item);
                        }
                    }

                    if (newQueue.Count > 0)
                    {
                        _actorQueues[actorId] = newQueue;
                    }
                    else
                    {
                        _actorQueues.Remove(actorId);
                    }

                    if (foundAction != null)
                    {
                        foundAction.State = TacticalActionState.Cancelled;
                        foundAction.OnCancel();
                        ActionCancelled?.Invoke(this, new ActionEventArgs(foundAction, _globalTime));
                        return true;
                    }
                }
            }

            return false;
        }

        /// <inheritdoc />
        public int CancelActorActions(Guid actorId)
        {
            if (actorId == Guid.Empty)
            {
                return 0;
            }

            int count = 0;

            if (_activeActions.Remove(actorId, out var activeAction))
            {
                activeAction.State = TacticalActionState.Cancelled;
                activeAction.OnCancel();
                ActionCancelled?.Invoke(this, new ActionEventArgs(activeAction, _globalTime));
                count++;
            }

            if (_actorQueues.Remove(actorId, out var queue))
            {
                while (queue.Count > 0)
                {
                    var queuedAction = queue.Dequeue();
                    queuedAction.State = TacticalActionState.Cancelled;
                    queuedAction.OnCancel();
                    ActionCancelled?.Invoke(this, new ActionEventArgs(queuedAction, _globalTime));
                    count++;
                }
            }

            return count;
        }

        /// <inheritdoc />
        public IReadOnlyList<TacticalAction> GetActiveActions()
        {
            return _activeActions.Values
                .OrderBy(a => a.ActorId)
                .ThenBy(a => a.Id)
                .ToList()
                .AsReadOnly();
        }

        /// <inheritdoc />
        public IReadOnlyList<TacticalAction> GetQueuedActions(Guid actorId)
        {
            if (_actorQueues.TryGetValue(actorId, out var queue))
            {
                return queue.ToList().AsReadOnly();
            }

            return Array.Empty<TacticalAction>();
        }

        /// <inheritdoc />
        public TacticalAction? GetCurrentAction(Guid actorId)
        {
            return _activeActions.TryGetValue(actorId, out var action) ? action : null;
        }

        /// <inheritdoc />
        public void Tick(float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
            {
                throw new ArgumentException("Delta time (dt) must be strictly positive and finite.", nameof(dt));
            }

            // Advance physiology for all registered entities in deterministic order (by Id)
            var entities = _world.GetEntities();
            foreach (var entity in entities)
            {
                entity.Physiology?.TickPhysiology(dt);

                if (entity.Physiology != null && entity.Physiology.ConsciousnessLevel <= 0f)
                {
                    CancelActorActions(entity.Id);
                }
            }

            // Snapshot active actor IDs sorted deterministically
            var actorIds = _activeActions.Keys.OrderBy(id => id).ToList();

            foreach (var actorId in actorIds)
            {
                if (!_activeActions.ContainsKey(actorId))
                {
                    continue;
                }

                float remainingDt = dt;

                while (remainingDt > Epsilon)
                {
                    // Ensure active action exists or promote from queue
                    if (!_activeActions.TryGetValue(actorId, out var currentAction))
                    {
                        if (_actorQueues.TryGetValue(actorId, out var queue) && queue.Count > 0)
                        {
                            currentAction = queue.Dequeue();
                            _activeActions[actorId] = currentAction;
                            if (queue.Count == 0)
                            {
                                _actorQueues.Remove(actorId);
                            }
                        }
                        else
                        {
                            break; // No more actions for this actor
                        }
                    }

                    // Start action if pending
                    if (currentAction.State == TacticalActionState.Pending)
                    {
                        currentAction.State = TacticalActionState.Executing;
                        currentAction.StartTime = _globalTime + (dt - remainingDt);
                        currentAction.OnStart();
                        ActionStarted?.Invoke(this, new ActionEventArgs(currentAction, currentAction.StartTime));
                    }

                    float neededTU = currentAction.TUCost - currentAction.ExecutionProgress;

                    if (neededTU <= remainingDt + Epsilon)
                    {
                        // Action completes in this sub-step
                        float stepDt = MathF.Min(neededTU, remainingDt);
                        if (stepDt <= 0f)
                        {
                            stepDt = remainingDt;
                        }

                        currentAction.ExecutionProgress = currentAction.TUCost;
                        currentAction.State = TacticalActionState.Completed;
                        float completionTime = _globalTime + (dt - remainingDt) + stepDt;
                        currentAction.CompletionTime = completionTime;

                        bool failed = false;
                        try
                        {
                            currentAction.Execute(stepDt);
                        }
                        catch (Exception ex)
                        {
                            failed = true;
                            currentAction.State = TacticalActionState.Failed;
                            currentAction.FailureException = ex;
                            currentAction.OnFail(ex);
                            ActionFailed?.Invoke(this, new ActionFailedEventArgs(currentAction, ex, _globalTime + (dt - remainingDt)));
                            _activeActions.Remove(actorId);
                            break;
                        }

                        if (!failed)
                        {
                            currentAction.OnComplete();

                            ActionProgressed?.Invoke(this, new ActionProgressEventArgs(
                                currentAction,
                                stepDt,
                                currentAction.ExecutionProgress,
                                currentAction.TUCost,
                                completionTime));

                            ActionCompleted?.Invoke(this, new ActionEventArgs(currentAction, completionTime));

                            _activeActions.Remove(actorId);
                            remainingDt -= stepDt;
                        }
                    }
                    else
                    {
                        // Action requires more than remainingDt
                        currentAction.ExecutionProgress += remainingDt;
                        float progressTime = _globalTime + (dt - remainingDt) + remainingDt;

                        bool failed = false;
                        try
                        {
                            currentAction.Execute(remainingDt);
                        }
                        catch (Exception ex)
                        {
                            failed = true;
                            currentAction.State = TacticalActionState.Failed;
                            currentAction.FailureException = ex;
                            currentAction.OnFail(ex);
                            ActionFailed?.Invoke(this, new ActionFailedEventArgs(currentAction, ex, _globalTime + (dt - remainingDt)));
                            _activeActions.Remove(actorId);
                            break;
                        }

                        if (!failed)
                        {
                            ActionProgressed?.Invoke(this, new ActionProgressEventArgs(
                                currentAction,
                                remainingDt,
                                currentAction.ExecutionProgress,
                                currentAction.TUCost,
                                progressTime));

                            remainingDt = 0f;
                            break;
                        }
                    }
                }

                // If actor no longer has an active action, but has queued actions, promote the next one
                if (!_activeActions.ContainsKey(actorId) && _actorQueues.TryGetValue(actorId, out var remainingQueue) && remainingQueue.Count > 0)
                {
                    var next = remainingQueue.Dequeue();
                    _activeActions[actorId] = next;
                    if (remainingQueue.Count == 0)
                    {
                        _actorQueues.Remove(actorId);
                    }
                }
            }

            float prevTime = _globalTime;
            _globalTime += dt;
            TimeAdvanced?.Invoke(this, new TimeAdvancedEventArgs(dt, prevTime, _globalTime));
        }

        /// <inheritdoc />
        public void Reset()
        {
            _globalTime = 0.0f;
            _activeActions.Clear();
            _actorQueues.Clear();
        }
    }
}
