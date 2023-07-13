using ECM2.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using System.Collections;
using Core4;
using DG.Tweening;
using Asmdef4;
using Friedforfun.ContextSteering.PlanarMovement;

namespace ECM2.Characters
{
    public class AgentCharacter4 : Character{
        #region FIELDS (program-tweakable values)
        //Components of this gameObject
        SelfSchedulingPlanarController sc;                  //Component: context steering scheduling controller
        List<DotToTransform> dotToTarget_Behaviours;        //Component list: DotToTransform steering behaviours
        List<DotToTransformMask> dotToTarget_Masks;             //Component list: DotToTransform steering behaviours

        //Agent State
        AgentStateStruct ags;
        struct AgentStateStruct{
            public Transform longtermTarget;               //The overall longterm target the agent wants
            public bool agentNmHitValid;                   //Is The most recent nmHit of the agent valid?
            public NavMeshHit agentNmHit;                  //The most recent nmHit of the agent
            public Quaternion rigBase_baseRotation;        //Base rotation of rig to use look rotation with
            public Quaternion lastLookRotation;            //Where the agent was looking last frame
            public AgentNode agentNodePrev;                //Used to validate switches between state with animation events
            public AgentNode agentNode;                    //Used to determine how to update Agent every tick
            public int occupiedClusterIndex;

            public string agentNode_switch_log;            //DEBUG
        }
        public enum AgentNode{
            SleepPos =             -3,
            SleepPosRot =          -2,
            Sleep =                -1,
            Travel =                1,
            TravelFalling =         2,
        }
        public string External_GetAgentNodeSwitchLog() => ags.agentNode_switch_log;

        //Path State
        PathStateStruct pas;
        struct PathStateStruct{
            public NavMeshPath pathToTarget;               //A path to the target transform, only generated if pathing is necessary
            public int[] pathCornerAreas;                  //The areas each corner in a path are present in, used to determine jumping
            public int pathDestCornerArea;                 //The area of the last corner in a path are present in, used to determine repathing
            public bool pathUsing;                         //Whether or not a path is currently necessary to reach mainTargetTransform
            public bool pathCornerJumpNecessary;           //Whether or not the current corner requires a jump upon arrival to reach the next one
            public int pathCornerIdx;                      //Current index in corner array that we should use as target
            public PathNode pathNode;                      //Current state of a path in use
            public Transform cornerTarget;                 //The transform the context steer desires if using a path, which will lead to longtermTarget

            public float cornerReachDistance;              //How far from a corner the Agent can be to consider it reached
            public float cornerEdgeMergeDistance;          //How far from the closest edge point a corner can be to be considdered the same
            public float edgePushMaxDistance;              //How far from an edge will be a corner be pushed from at its maximum
            public float rBuffer;                          //Buffer used with the raycast's sourcePosition because it starts from an edge and might hit itself

            public float TIMELEFT_GENPATH;                  //The amount of time left to generate a new path
            public float TIMELEFT_USEPATH;                  //The amount of time left to follow the current path
            public float TIMELEFT_JUMPINGCORNER;            //The amount of time left to jump the current corner to the next one

            public float COOLLEFT_JUMP;                     //Amount of time left for a jump cooldown

            public string pathNode_switch_log;             //DEBUG
        }
        public enum PathNode{
            Unused =                   -1,
            Generating =                1,
            ApproachingCorner =         2,
            ApproachingCornerJump =     3,
            JumpingCorner =             4,
        }
        public string External_GetPathNodeSwitchLog() => pas.pathNode_switch_log;

        const float TIME_GENPATH =          3;
        const float TIME_USEPATH =          10;
        const float TIME_JUMPINGCORNER =    2f;

        const float COOL_JUMP =             1f;
        #endregion

        #region PROPERTIES (human-tweakable values)
        [Header("Context Steering")]
        [SerializeField]
        [Tooltip("Minimum magnitude of mostDesired moveVector from _sc required to perform movement")]
        float desireThreshold;
        [SerializeField]
        [Tooltip("Minimum magnitude of mostDesired moveVector from _sc required to perform fast movement.")]
        float fastThreshold;
        [SerializeField]
        [Tooltip("Stare at the target within this radius to it.")]
        float stareRadius;
        [SerializeField]
        [Tooltip("Rig base used to perform visual rotation.")]
        Transform rigBase;
        #endregion
        #region ANIMATION EVENT METHODS
        protected void ae_AgentNode_TempSleep_Pos_Start(){
            if(ags.agentNode == AgentNode.SleepPos || ags.agentNode == AgentNode.SleepPosRot)
                Debug.LogError("Attempted to start a new TempSleep while one is already started.");

            ags.agentNodePrev = ags.agentNode;
            Agent_EnterNode(AgentNode.SleepPos, "animation event sleep pos");
            SetMovementDirection(Vector3.zero);
        }

        protected void ae_AgentNode_TempSleep_PosRot_Start(){
            if(ags.agentNode == AgentNode.SleepPosRot)
                Debug.LogError("Attempted to start a new TempSleep while one is already started.");
            
            //When transitioning from AtkStop Pos to PosRot, don't store new _agentNodePrev
            if(ags.agentNode != AgentNode.SleepPos)
                ags.agentNodePrev = ags.agentNode;
            Agent_EnterNode(AgentNode.SleepPosRot, "animation event sleep pos rot");            
            SetMovementDirection(Vector3.zero);
        }

        protected void ae_AgentNode_TempSleep_End(){
            if(ags.agentNode != AgentNode.SleepPos && ags.agentNode != AgentNode.SleepPosRot)
                return;

            Agent_EnterNode(ags.agentNodePrev, "animation event sleep end");            
        }
        #endregion
        #region METHODS

        //Set context behaviour/mask transforms to the Agent's target
        void Global_SetContextTargetTransforms(Transform newTarget){
            foreach(DotToTransform sb in dotToTarget_Behaviours){
                List<Transform> sb_positions = sb.GetPositions();
                sb_positions.Clear();

                if(newTarget == null)
                    continue;
                else
                    sb_positions.Add(newTarget);
            }

            foreach(DotToTransformMask sm in dotToTarget_Masks){
                List<Transform> sb_positions = sm.GetPositions();
                sb_positions.Clear();

                if(newTarget == null)
                    continue;
                else
                    sb_positions.Add(newTarget);
            }
        }

        void Agent_EnterNode(AgentNode newNode, string description){
            string newLog = String.Format(
                "{0,-7:F1} s : entered {1} because \"{2}\".\n",
                Time.realtimeSinceStartup,
                Enum.GetName(typeof(AgentNode), newNode),
                description
            );
            ags.agentNode_switch_log += newLog;
            
            ags.agentNode = newNode;
        }

        void Agent_ExecuteNode(){ switch(ags.agentNode){
            case AgentNode.SleepPos:
            case AgentNode.SleepPosRot:
            case AgentNode.Sleep:
                break;
            case AgentNode.Travel:
                switch(pas.pathNode){
                case PathNode.Unused:
                case PathNode.ApproachingCorner:
                case PathNode.ApproachingCornerJump:
                    Agent_TravelGrounded();
                    break;
                case PathNode.JumpingCorner:
                    Agent_TravelJumpingCorner();
                    break;
                }
                Agent_DetermineTravelMethod();
                break;
        }}

        //Sample Agent's current nmPosition, and importantly, its nmArea
        bool Agent_UpdateNavmeshHit(){
            if(!IsOnWalkableGround()) return false;
            if(!NavMesh.SamplePosition(transform.position, out ags.agentNmHit, .1f, NavMesh.AllAreas)){
                Debug.LogError(name + "is grounded but cannot determine nmArea at the moment.");
                return false;
            }
            return true;
        }

        void Agent_DetermineTravelMethod(){
            ags.agentNmHitValid = Agent_UpdateNavmeshHit();
            if(!ags.agentNmHitValid) return;
            Transform hardCodedTarget = CoreData.Instance.Player.transform;

            //PATH TRAVEL
            if(Agent_IsPathNecessary(hardCodedTarget)){
                //...And we are already using a path and a repath isnt necessary,  do nothing
                if(pas.pathUsing && !Agent_IsRepathNecessary()) return;
                //Otherwise, set a new path to the target
                else Agent_SetNewPath(hardCodedTarget);
                return;
            }

            //DIRECT TRAVEL
            //If our current target is the same as the new one, do nothing
            if(ags.longtermTarget != null && ags.longtermTarget == hardCodedTarget && pas.pathUsing == false) return;

            Path_ClearPathState();
            Path_EnterNode(PathNode.Unused, "path not necessary to reach new target");
            
            //Otherwise, set a new target to be context steered toward
            ags.longtermTarget = hardCodedTarget;
            Global_SetContextTargetTransforms(hardCodedTarget);

            if(ags.agentNode != AgentNode.Travel)
                Agent_EnterNode(AgentNode.Travel, "travel directly");
        }

        // A repath is necessary if the player moved to another nmArea while a path already is being used
        bool Agent_IsRepathNecessary(){
            if(!CoreData.Instance.FoeManager.PlayerGroundedToNavMesh()) return false;

            return pas.pathDestCornerArea != CoreData.Instance.FoeManager.areaPlayerOccupies;
        }

        // Determine if a path is necessary to reach newTargetTransform...
        // ...if so, generate a path, otherwise we can traverse directly for now
        bool Agent_IsPathNecessary(Transform newTarget){
            //Path is necessary if...
            //...The player is on a different NavMeshArea than our Agent...
            if(ags.agentNmHit.mask != CoreData.Instance.FoeManager.areaPlayerOccupies){
                return true;
            } else{
                //...Or if they ARE on the same NavMeshArea...
                //...but the Agent can't cast along that nmArea to the player without collision.
                NavMeshHit rayHit;
                if(NavMesh.Raycast(ags.agentNmHit.position, CoreData.Instance.FoeManager.navmeshPositionPlayer, out rayHit, ags.agentNmHit.mask)){
                    return true;
                } else{
                    return false;
                }
            }
        }

        void Agent_SetNewPath(Transform newTarget){
            //Clear path
            pas.pathToTarget.ClearCorners();

            //Sleep agent until coroutine below generates path and sets agent live
            Agent_EnterNode(AgentNode.Sleep, "will wait to calculate path");

            //Use a coroutine to attempt to calculate a path (must wait for player to be grounded on nmArea)
            StartCoroutine(Path_CalculateNewPathWhenPossible(newTarget.position, NavMesh.AllAreas));
        }

        IEnumerator Path_CalculateNewPathWhenPossible(Vector3 targetPosition, int areaMask){
            while(!CoreData.Instance.FoeManager.PlayerGroundedToNavMesh()) yield return null;

            //Attempt to calculate a path
            if(!NavMesh.CalculatePath(ags.agentNmHit.position, CoreData.Instance.FoeManager.navmeshPositionPlayer, areaMask, pas.pathToTarget)){
                Debug.LogError("Path was determined necessary, but pathing failed.");
            } else{
                Agent_EnterNode(AgentNode.Travel, "travel by path");
                Path_EnterNode(PathNode.Generating, "path started generating");

                Path_InitializePathState();
                pas.pathDestCornerArea = CoreData.Instance.FoeManager.areaPlayerOccupies;
                Global_SetContextTargetTransforms(pas.cornerTarget);
            }
        }

        void Agent_TravelGrounded(){
            //Get most desired movement direction+magnitude
            Vector3 mostDesired = sc.MoveVector();
            //If the magnitude is below a desireThreshold, don't move
            if(mostDesired.magnitude < desireThreshold){
                mostDesired = Vector3.zero;
            } else{
            //Set movement direction as mostDesired dir, with either a slow or fast magnitude based on the mostDesired magnitude
                mostDesired = (mostDesired.magnitude < fastThreshold ?  minAnalogWalkSpeed : maxWalkSpeed) * mostDesired.normalized;
            }

            SetMovementDirection(mostDesired);
        }

        void Agent_TravelJumpingCorner(){

            //Spam press jump button
            if(IsOnWalkableGround() && pas.COOLLEFT_JUMP < 0){
                StartCoroutine(Agent_JumpThenRelease());
                pas.COOLLEFT_JUMP = COOL_JUMP;
            }

            //We want to jump toward the next corner (where we should be after the jump)
            Vector3 mostDesired = pas.pathToTarget.corners[pas.pathCornerIdx + 1] - transform.position;
            mostDesired.y = 0f;

            //If the magnitude is below a desireThreshold, don't move
            if(mostDesired.magnitude < desireThreshold){
                mostDesired = Vector3.zero;
            } else{
            //Set movement direction as mostDesired dir, with either a slow or fast magnitude based on the mostDesired magnitude
                mostDesired = (mostDesired.magnitude < fastThreshold ?  minAnalogWalkSpeed : maxWalkSpeed) * mostDesired.normalized;
            }

            SetMovementDirection(mostDesired);

            pas.COOLLEFT_JUMP -= Time.fixedDeltaTime;
        }

        //Necessary to perform a jump as you have to call StopJumping()
        IEnumerator Agent_JumpThenRelease(){
            Jump();
            yield return new WaitForEndOfFrame();
            StopJumping();
        }

        void Path_EnterNode(PathNode newNode, string description){
            string newLog = String.Format(
                "{0,-7:F1} s : entered {1} because \"{2}\".\n",
                Time.realtimeSinceStartup,
                Enum.GetName(typeof(PathNode), newNode),
                description
            );
            pas.pathNode_switch_log += newLog;
            
            pas.pathNode = newNode;
        }

        void Path_ExecuteNode(){ switch(pas.pathNode){
            case PathNode.Generating:
            if(pas.pathToTarget.status == NavMeshPathStatus.PathComplete){
                Path_FinishInitializePathState();
                Path_EnterNode(PathNode.ApproachingCorner, "path finished generating");
            }
            break;
            case PathNode.ApproachingCorner:
                Path_AdvanceApproachingCorner();
            break;
            case PathNode.ApproachingCornerJump:
                Path_AdvanceApproachingCornerJump();
            break;
            case PathNode.JumpingCorner:
                Path_AdvanceJumpingCorner();
            break;
            case PathNode.Unused:
            break;
        }}

        void Path_FillCornerAreas(){
            for(int i = 0; i < pas.pathCornerAreas.Length; i++){
                NavMeshHit cornerHit;
                if(!NavMesh.SamplePosition(pas.pathToTarget.corners[i], out cornerHit, 1f, NavMesh.AllAreas)){
                    Debug.LogError("Cannot determine current corner's area");
                }
                pas.pathCornerAreas[i] = cornerHit.mask;
            }
        }

        //A jump will be necessary upon arriving at this new corner, if the corner AFTER this new corner is in a diff nmArea
        bool Path_IsJumpNecessaryAtCorner(int cornerIdx){
            return(cornerIdx + 1 < pas.pathToTarget.corners.Length)
                   &&
                   pas.pathCornerAreas[cornerIdx] != pas.pathCornerAreas[cornerIdx + 1];
        }

        //A corner is reaachable if the agent is within cornerReachDistance AND on the same nmArea
        bool Path_IsCornerInReach(int cornerIdx){
            if(!ags.agentNmHitValid) return false;
            return
                (transform.position - pas.pathToTarget.corners[cornerIdx]).magnitude <= pas.cornerReachDistance
                &&
                ags.agentNmHit.mask == pas.pathCornerAreas[cornerIdx];
        }

        //Determine if the Agent has reaached a new corner, and handle it
        void Path_AdvanceApproachingCorner(){

            bool reachedNewCorner = false;
            
            //If the current corner is in reach...
            while(Path_IsCornerInReach(pas.pathCornerIdx)){

                //End pathing is it is the final corner...
                if(pas.pathCornerIdx + 1 >= pas.pathToTarget.corners.Length){
                    Path_ClearPathState();
                    Path_EnterNode(PathNode.Unused, "reached final corner of path");
                    Global_SetContextTargetTransforms(null);
                    return;
                } //Otherwise...

                //Advance the corner idx
                reachedNewCorner = true;
                pas.pathCornerIdx++;

                //Find if a jump is necessary at this corner, enter a new node and break...
                if(pas.pathCornerJumpNecessary = Path_IsJumpNecessaryAtCorner(pas.pathCornerIdx))
                {
                    Path_EnterNode(PathNode.ApproachingCornerJump, "current->next corner requires jump");
                    break;
                }
                //..Otherwise, attempt to edge push the corner (we don't want to edge push corners that require a jump)
                else{
                    Path_DetermineCornerEdgePush();
                }
            }
            if(!reachedNewCorner) return;

            //Update the cornerTargets position to the new corner
            pas.cornerTarget.position = pas.pathToTarget.corners[pas.pathCornerIdx];
        }

        void Path_AdvanceApproachingCornerJump(){
            if(Path_IsCornerInReach(pas.pathCornerIdx)){

                pas.TIMELEFT_JUMPINGCORNER = TIME_JUMPINGCORNER;
                Path_EnterNode(PathNode.JumpingCorner, "reached corner w/ necessary jump");
            }
        }
        
        void Path_AdvanceJumpingCorner(){
            if(pas.TIMELEFT_JUMPINGCORNER < 0){
                Path_ClearPathState();
                Path_EnterNode(PathNode.Unused, "TIME_JUMPINGCORNER expired");
                return;
            }

            if(Path_IsCornerInReach(pas.pathCornerIdx + 1)){
                pas.pathCornerIdx++;
                pas.cornerTarget.position = pas.pathToTarget.corners[pas.pathCornerIdx];
                Path_EnterNode(PathNode.ApproachingCorner, "succeeded jumping corner to next");
                return;
            }

            pas.TIMELEFT_JUMPINGCORNER -= Time.fixedDeltaTime;
        }

        //Determine if a corner is on the edge of a navmesh surface, and if so, push it away from the edge for less corner hugging
        void Path_DetermineCornerEdgePush(){
            NavMeshHit edgeHit;
            if(NavMesh.FindClosestEdge(pas.pathToTarget.corners[pas.pathCornerIdx], out edgeHit, ags.agentNmHit.mask)){
                if((pas.pathToTarget.corners[pas.pathCornerIdx] - edgeHit.position).magnitude <= pas.cornerEdgeMergeDistance){
                    NavMeshHit edgePushHit;
                    float edgePushMaxDistancePossible = pas.edgePushMaxDistance;
                    Vector3 sourcePosition = pas.pathToTarget.corners[pas.pathCornerIdx] + (edgeHit.normal * pas.rBuffer);
                    if(NavMesh.Raycast(sourcePosition,
                    pas.pathToTarget.corners[pas.pathCornerIdx] + edgeHit.normal * pas.edgePushMaxDistance, out edgePushHit, ags.agentNmHit.mask)){
                        edgePushMaxDistancePossible = edgePushHit.distance;
                    }
                    pas.pathToTarget.corners[pas.pathCornerIdx] += edgeHit.normal * edgePushMaxDistancePossible;
                }
            }
        }

        
        void Path_InitializePathState(){
            pas.pathCornerIdx = 0;
            pas.pathUsing = true;
        }
        
        void Path_FinishInitializePathState(){
            pas.pathCornerAreas = new int[pas.pathToTarget.corners.Length];
            Path_FillCornerAreas();
            pas.pathCornerJumpNecessary = Path_IsJumpNecessaryAtCorner(pas.pathCornerIdx);
            pas.cornerTarget.position = pas.pathToTarget.corners[pas.pathCornerIdx];
        }
        
        void Path_ClearPathState(){
            pas.pathToTarget = new NavMeshPath();
            pas.pathCornerAreas = null;
            pas.pathDestCornerArea = -1;
            pas.pathUsing = false;
            pas.pathCornerJumpNecessary = false;
            pas.pathCornerIdx = -1;

            pas.TIMELEFT_GENPATH = 0;
            pas.TIMELEFT_USEPATH = 0;
            pas.TIMELEFT_JUMPINGCORNER = 0;
            pas.COOLLEFT_JUMP = 0;
        }

        protected override void OnMove(){
            //Execute Agent and Path nodes
            Agent_ExecuteNode();
            if(pas.pathUsing) Path_ExecuteNode();

            base.OnMove();
        }

        protected override void OnLateUpdate(){
            Path_DebugDrawPath();
        }

        void Path_DebugDrawPath(){
            if(!pas.pathUsing || pas.pathToTarget.status != NavMeshPathStatus.PathComplete) return;

            for (int i = 0; i < pas.pathToTarget.corners.Length; i++){
                NavMeshHit nmHit2;
                NavMesh.SamplePosition(pas.pathToTarget.corners[i], out nmHit2, .1f, NavMesh.AllAreas);

                Color pointCol = Color.grey;
                switch(nmHit2.mask){
                    case (int)A.Walkable: pointCol = Color.blue * .5f; break;
                    case (int)A.Walkable2: pointCol = Color.red * .5f; break;
                }
                DebugExtension.DebugPoint(pas.pathToTarget.corners[i], pointCol);
                if(i+1 < pas.pathToTarget.corners.Length)
                    Debug.DrawLine(pas.pathToTarget.corners[i], pas.pathToTarget.corners[i + 1], Color.grey);
            }
                DebugExtension.DebugPoint(pas.pathToTarget.corners[pas.pathCornerIdx], Color.green, .5f, 0f, false);
                if(pas.pathNode == PathNode.JumpingCorner)
                DebugExtension.DebugPoint(pas.pathToTarget.corners[pas.pathCornerIdx + 1], Color.yellow, .5f, 0f, false);
        }

        protected override void OnStart(){
            base.OnStart();

            sc = GetComponentInChildren<SelfSchedulingPlanarController>();
            if(sc == null) Debug.LogError(name + " has no SelfSchedulingPlanarController found in children.");

            SetMovementMode(MovementMode.Walking);

            //Set field defaults
            pas = new PathStateStruct();
            ags = new AgentStateStruct();

            ags.agentNodePrev = AgentNode.Sleep;
            ags.lastLookRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            ags.occupiedClusterIndex = -1;

            if(rigBase == null) Debug.LogError(name + " must assign rigBase.");
            ags.rigBase_baseRotation = rigBase.localRotation;
            ags.agentNode = AgentNode.Travel;

            pas.pathToTarget = new NavMeshPath();
            pas.pathCornerIdx = -1;
            pas.pathDestCornerArea = -1;
            GameObject ctGO = new GameObject("NavMesh Corner Target");
            pas.cornerTarget = ctGO.transform;
            pas.cornerEdgeMergeDistance = .3f;
            pas.edgePushMaxDistance = 2f;
            pas.rBuffer = 0.05f;
            pas.cornerReachDistance = 1f;
            
            if(CoreData.referencesValid) OnReferencesValid();
            else CoreData.OnReferencesStored += OnReferencesValid;
        }

        void OnReferencesValid(){
            dotToTarget_Behaviours = new List<DotToTransform>();
            foreach(PlanarSteeringBehaviour sb in sc.GetBehaviours()){
                if(!sb.BehaviourName.Contains("Target")) continue;
                
                //Add to list of _dotToTargets
                DotToTransform dotToTarget = (DotToTransform)sb;
                dotToTarget_Behaviours.Add(dotToTarget);
            }

            dotToTarget_Masks = new List<DotToTransformMask>();
            foreach(PlanarSteeringMask sm in sc.GetMasks()){
                if(!sm.MaskName.Contains("Target")) continue;
                
                //Add to list of _dotToTargets
                DotToTransformMask dotToTarget = (DotToTransformMask)sm;
                dotToTarget_Masks.Add(dotToTarget);
            }
            Global_SetContextTargetTransforms(CoreData.Instance.Player.transform);
        }

        /// If overriden, must call base method in order to fully initialize the class.
        protected override void OnReset(){
            // Base class defaults
            base.OnReset();

            //Set property defaults 
            stareRadius = 15f;
        }
        #endregion
        #region MOVEMENT OVERRIDES

        // float rotSpeed = 360f; //deg/s
        protected override Vector3 CalcVelocity(Vector3 velocity, Vector3 desiredVelocity, float friction, bool isFluid = false)
        {
            //Desired Velocity = movementDirection * GetMaxSpeed()
            // Compute requested move direction

            //Magnitude of desiredVelocity ignored here. It seems to give better results for avoidance
            //remove "/ desiredSpeed" to utilize agent's avoidance velocity changes
            float desiredSpeed = desiredVelocity.magnitude;
            Vector3 desiredMoveDirection = desiredSpeed > 0.0f ? desiredVelocity : Vector3.zero;

            // Requested acceleration (factoring analog input, AKA desiredSpeed/maxSpeed)
            float analogInputModifier = ComputeAnalogInputModifier(desiredVelocity);
            Vector3 requestedAcceleration = GetMaxAcceleration() * analogInputModifier * desiredMoveDirection;

            // Actual max speed (factoring analog input)
            float actualMaxSpeed = Mathf.Max(GetMinAnalogSpeed(), GetMaxSpeed() * analogInputModifier);

            // Friction
            // Only apply braking if there is no input acceleration,
            // or we are over our max speed and need to slow down to it

            bool isZeroAcceleration = requestedAcceleration.isZero();
            bool isVelocityOverMax = velocity.isExceeding(actualMaxSpeed);

            if (isZeroAcceleration || isVelocityOverMax)
            {
                // Pre-braking velocity

                Vector3 oldVelocity = velocity;

                // Apply friction and braking

                float actualBrakingFriction = useSeparateBrakingFriction ? brakingFriction : friction;
                //TODO: figure out why friction is so weird
                velocity = ApplyVelocityBraking(velocity, 999f, GetBrakingDeceleration());

                // Don't allow braking to lower us below max speed if we started above it

                if (isVelocityOverMax && velocity.sqrMagnitude < MathLib.Square(actualMaxSpeed) &&
                    Vector3.Dot(requestedAcceleration, oldVelocity) > 0.0f)
                    velocity = oldVelocity.normalized * actualMaxSpeed;
            }
            else
            {
                // Friction, this affects our ability to change direction

                velocity -= (velocity - desiredMoveDirection * velocity.magnitude) * Mathf.Min(friction * Time.deltaTime, 1.0f);
            }

            // Apply fluid friction

            if (isFluid)
                velocity *= 1.0f - Mathf.Min(friction * Time.deltaTime, 1.0f);

            // Apply acceleration

            if (!isZeroAcceleration)
            {
                float newMaxSpeed = velocity.isExceeding(actualMaxSpeed) ? velocity.magnitude : actualMaxSpeed;

                velocity += requestedAcceleration * Time.deltaTime;
                velocity = velocity.clampedTo(newMaxSpeed);
            }

            // Return new velocity

            return velocity;
        }

        //Perform look rtoation, where to look is based on the agent's state
        void RotateByState(Vector3 mvmtDir){
            Vector3 desiredLookDir = mvmtDir;
            switch(ags.agentNode){
                //return
                case AgentNode.SleepPosRot:
                case AgentNode.Sleep:
                    return;
                //close enough to player ? player : mvmtDir
                case AgentNode.SleepPos:
                case AgentNode.Travel:
                    Vector3 towardPlayer = (CoreData.Instance.Player.transform.position - transform.position);
                    if(towardPlayer.magnitude <= stareRadius){
                        desiredLookDir = towardPlayer;
                    } else if(mvmtDir == Vector3.zero) return;
                    break;
            }
            ags.lastLookRotation = rigBase.rotation;

            //Rotate toward target rotation using RotateTowards() which has a max degree delta
            Quaternion targetRotation = Quaternion.LookRotation(desiredLookDir, Vector3.up) * ags.rigBase_baseRotation;
            rigBase.rotation = Quaternion.RotateTowards(rigBase.rotation, targetRotation, rotationRate * Time.deltaTime);
        }

        protected override void UpdateRotation(){
            if(IsDisabled()) return;
            RotationMode rotationMode = GetRotationMode();
            if (rotationMode == RotationMode.None)
                return;

            if (rotationMode == RotationMode.OrientToMovement){
                RotateByState(GetMovementDirection());
            }
            else if (rotationMode == RotationMode.OrientToCameraViewDirection){
                Debug.LogError("Invalid Rotation Mode for AgentCharacter4");
            }
            else if (rotationMode == RotationMode.OrientWithRootMotion){
                if(rootMotionController) ApplyRootMotionRotation();
            }
        }
        #endregion
    }
}
