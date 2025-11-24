using Unity.Entities.UniversalDelegates;

namespace Doc.CodeSamples.SyBase.Tests
{
    using Unity.Entities;
    using Unity.Jobs;
    using Unity.Transforms;
    using Unity.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using Unity.Mathematics;
    using Unity.Burst;

    #region basic-system

    public struct Position : IComponentData
    {
        public float3 Value;
    }

    public struct Velocity : IComponentData
    {
        public float3 Value;
    }

    [RequireMatchingQueriesForUpdate]
    public partial class ECSSystem : SystemBase
    {
        [BurstCompile]
        public partial struct ExampleJob : IJobEntity
        {
            public float DeltaTime;
            
            public void Execute(ref Position position, in Velocity velocity)
            {
                position.Value += velocity.Value * DeltaTime;
            }
        }

        protected override void OnUpdate()
        {
            // Create and schedule the job
            var job = new ExampleJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            };

            job.ScheduleParallel();
        }
    }

    #endregion

    #region basic-job

    public partial class JobSystem : SystemBase
    {
        [BurstCompile]
        struct FibonacciJob : IJob
        {
            public NativeArray<int> Sequence;

            public void Execute()
            {
                Sequence[0] = Sequence[Sequence.Length - 2];
                Sequence[1] = Sequence[Sequence.Length - 1];
                for (int i = 2; i < Sequence.Length; i++)
                {
                    Sequence[i] = Sequence[i - 1] + Sequence[i - 2];
                }
            }
        }

        NativeArray<int> EndlessSequence;

        protected override void OnUpdate()
        {
            if (!EndlessSequence.IsCreated)
            {
                EndlessSequence = new NativeArray<int>(1000, Allocator.Persistent);
                EndlessSequence[EndlessSequence.Length - 2] = 1;
                EndlessSequence[EndlessSequence.Length - 1] = 1;
            }

            var job = new FibonacciJob
                {
                Sequence = EndlessSequence
            };
            job.Schedule();
        }

        protected override void OnDestroy()
        {
            if (EndlessSequence.IsCreated)
                EndlessSequence.Dispose();
        }
    }

    #endregion

    public struct WritableComponent : IComponentData
    {
    }

    public struct ReadonlyComponent : IComponentData
    {
    }

    public struct AComponent : IComponentData
    {
    }

    public struct AnotherComponent : IComponentData
    {
    }

    [RequireMatchingQueriesForUpdate]
    public partial class SimpleDependencyManagement : SystemBase
    {
        #region simple-dependency

        [BurstCompile]
        partial struct JobOne : IJobEntity
        {       
            public void Execute(in AComponent c)
            {
                /*...*/
            }
        }

        [BurstCompile]
        partial struct JobTwo : IJobEntity
        {
            public void Execute(in AnotherComponent c)
                {
                    /*...*/
            }
        }

        [BurstCompile]
        struct JobThree : IJob
        {
            [DeallocateOnJobCompletion] public NativeArray<int> Result; // Automatically disposed when job completes

            public void Execute()
                {
                    /*...*/
                Result[0] = 1;
            }
        }

        protected override void OnUpdate()
        {
            // Implicit dependency chaining: each ScheduleParallel updates SystemBase.Dependency
            new JobOne().ScheduleParallel();
            new JobTwo().ScheduleParallel();

            // Final job depends on previous via implicit Dependency and disposes its NativeArray automatically
            var jobThree = new JobThree
                {
                Result = new NativeArray<int>(1, Allocator.TempJob)
            };
            jobThree.Schedule();
        }

        #endregion
    }

    [RequireMatchingQueriesForUpdate]
    public partial class ManualDependencyManagement : SystemBase
    {
        #region manual-dependency

        [BurstCompile]
        partial struct JobOne : IJobEntity
        {
            public void Execute(in AComponent c)
                {
                    /*...*/
            }
        }

        [BurstCompile]
        partial struct JobTwo : IJobEntity
        {
            public void Execute(in AnotherComponent c)
                {
                    /*...*/
            }
        }

        [BurstCompile]
        struct JobThree : IJob
        {
            [DeallocateOnJobCompletion] public NativeArray<int> Result; // Automatically disposed when job completes

            public void Execute()
            {
                /*...*/
                Result[0] = 1;
            }
        }

        protected override void OnUpdate()
        {
            // Explicitly opt-out of implicit chaining by scheduling with incoming system Dependency
            JobHandle One = new JobOne().ScheduleParallel(this.Dependency);
            JobHandle Two = new JobTwo().ScheduleParallel(this.Dependency);

            JobHandle intermediateDependencies =
                JobHandle.CombineDependencies(One, Two);

            var jobThree = new JobThree
                {
                Result = new NativeArray<int>(1, Allocator.TempJob)
            };

            JobHandle finalDependency = jobThree.Schedule(intermediateDependencies);

            // Propagate combined dependency to subsequent systems
            this.Dependency = finalDependency;
        }

        #endregion
    }
}
