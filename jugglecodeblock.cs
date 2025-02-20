using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ECM2.Characters;
using Core4;
using static ECM2.Characters.HackySacker4;

namespace Asmdef4
{
public class HackySack : MonoBehaviour
{

    #region FIELDS (program-tweakable values) ("_" prefix = component)
        //This HackySack's rigidbody
        Rigidbody _rb;
        //The current frame's cumulative position to be used with _rb.MovePosition() after being compounded
        Vector3 rbPos;
        //This HackySack's renderer GameObject
        Renderer _r;
        //This HackySack's Audio Source
        AudioSource _as;

        //The rotational velocity of the renderer, how it will spin every frame
        Vector3 rendererRotV;

        //Desired velocity of HackySack
        Vector3 desiredVelocity;

        //Modifiers to the Physical state
        public enum PhysicalModifier {
            None,                       //Normal gravity
            StraightShot,               //% of normal gravity for straightLength seconds via StraightShotTimer()
            Bunt,                       //Variable gravity for ballistic trajectory
            FloorBunt,                  //Bunt with no bunt magnetism, for floor
            Bounceback,                 //Variable gravity for ballistic trajectory, no bunt magnetism
            SurfaceHit                  //Variable gravity for a surface hit

        }
        PhysicalModifier physicalModifier;
        Vector3 surfaceHitGravity;
        Vector3 bouncebackGravity;
        Vector3 buntGravity;

        //Evaluated value from buntCurve
        float buntModifier;
        float buntModifierNormalized;

        //Evaluated value from buntModifier:pctAtMaxSpeed
        float trickFeelRatio;

        //Used for reducing bunt speed if within justBuntedNormalWithInputWindow
        float bunt_lastTimeBuntedWithInput;

        public delegate void NotifyShootInputReleased();

        //Used for applying how long a shot input was held for
        float shotHoldPct;
        //Used for slowing a shot over time during shotStraightTime
        float shotSpeedInit;
        //Used for slowing a shot over time during shotStraightTime
        float shotStraightPct;
        //Used for deadening a shot after exceeding its shotEffectiveRange
        Vector3 shotPosInit;
        
        struct PhysicalStats{
            public int surfaceHitCounter;
            public bool justShot;
        }
        PhysicalStats physicalStats;

        public enum State {
            Physical,
            Tweening,
            Reacting
        }

        //Current state of this HackySack
        State _state;
        State state{
            get{
                return _state;
            }
            set{
                if(_state == value){ //Same state
                    return;
                }
                else if(_state == State.Physical){ //Physical -> Any other state
                    //Reset physicalModifier for next time state is Physical
                    physicalModifier = PhysicalModifier.None;
                }
                _state = value;                
            }
        }

        //Tween for initializing the position of the HackySack for Juggle or Use
        RbLiveTransformTween _tweenInit;
        TransformLiveTransformTween _tweenShoot;


        //Hitstop vars
        bool hitstopping;
        float hitstopLengthHit;
        float hitstopLengthJuggle;
        float hitstopTrauma;
    #endregion
    #region PROPERTIES
        [Header("Gravity")]
            [Tooltip("Gravity during normal HackySack flight.")]
            [SerializeField]
            Vector3 normalGravity;
            [Tooltip("Gravity during StraightShot flight. Should be less than normal gravity.")]
            [SerializeField]
            Vector3 straightGravity;
            [Tooltip("Enforce gravity minimum during bunt BalTraj flight. Will make sack travel less distance if corrected, if ratio/buntCurve is set well, should not be noticeable.")]
            [SerializeField]
            bool enforceBuntGravityMin;
            [Tooltip("Gravity minimum during bunt BalTraj flight. Clamps dynamically calculatd BalTraj gravity.")]
            [SerializeField]
            Vector3 buntGravityMin;
            [Tooltip("Viewport Y visibly reached when the sack is bunted or bounceback'd.")]
            [SerializeField]
            [Range(0f, 1f)]
            float ceilingV;

        [Header("Surface Behavior")]
            [Tooltip("Angle (degrees upward) a surface reflection is desired to be at. A reflection angle is given makeup to try and achieve this.")]
            [SerializeField]
            float reflectAngleMin;
            [Tooltip("Angle (degrees FROM world up) maximum in which a surface is considered a floor.")]
            [SerializeField]
            [Rename("Floor Surface Angle Max")]
            float maxFSA;
            [Tooltip("% of lateral velocity that remains after hitting floor.")]
            [Range(0f,1f)]
            [SerializeField]
            float floorFrictionLeftover_Lat;
            [Tooltip("% of vertical velocity that remains after hitting floor.")]
            [Range(0f,1f)]
            [SerializeField]
            float floorFrictionLeftover_Vert;
            [Tooltip("Ratio minimum of Gravity:LateralVelocity during surface BalTraj calculation. Ensures nice looking/feeling trajectory.")]
            [SerializeField]
            [Rename("BalTraj Grav:LatV Min")]
            float ratio_Surface_G_To_L_min;

        [Header("Bunt")]
            [Tooltip("Used to determine lateral speed and distance during a bunt. Ranges from [0-1] in both X and Y.")]
            [SerializeField]
            AnimationCurve buntCurve;
            [Tooltip("Scales buntCurve from its [0-1] range to something more reasonable related to world units.")]
            [SerializeField]
            float buntModifierScale;
            [Tooltip("Time window (s) since last bunt with input as to which lateral speed will be reduced to prevent it flying off screen.")]
            [SerializeField]
            float justBuntedNormalWithInputWindow;
            [Tooltip("Ratio threshold of Lateral:PlayerMax speed in which lateral speed and maximum height for a bunt is changed to improve control and feel. It happens when looking down.")]
            [SerializeField]
            [Range(0,1)]
            float trickfeelThreshold;
            [Tooltip("Ratio minimum of Gravity:LateralVelocity during bunt (and bounceback) BalTraj calculation. Ensures nice looking/feeling trajectory.")]
            [SerializeField]
            [Rename("BalTraj Grav:LatV Min")]
            float ratio_Bunt_G_To_L_min;

        [Header("Shooting")]
            [Tooltip("Time (s) one can hold the shot input down for to charge it.")]
            [SerializeField]
            [Range(.1f, 10f)]
            public float shotHoldSeconds;
            [Tooltip("Distance from shot point (m) a sack's shot is effective for at min speed, before it deadens its speed dramatically.")]
            [SerializeField]
            public float shotEffectiveRangeMin;
            [Tooltip("Distance from shot point (m) a sack's shot is effective for at max speed, before it deadens its speed dramatically.")]
            [SerializeField]
            public float shotEffectiveRangeMax;
            [Tooltip("Angle (degrees upward) to shoot HackySack at, at min speed. The sack fires from below the center of the view, so usually should be upward. A slow sack needs a higher angle, this will be larger.")]
            [SerializeField]
            float shotAngle_SpeedMin;
            [Tooltip("Angle (degrees upward) to shoot HackySack at, at max speed. The sack fires from below the center of the view, so usually should be upward.")]
            [SerializeField]
            float shotAngle_SpeedMax;
            [Tooltip("Min Speed (u/s) to init a shot at.")]
            [SerializeField]
            float shotSpeedMin;
            [Tooltip("Max Speed (u/s) to init a shot at.")]
            [SerializeField]
            float shotSpeedMax;
            [Tooltip("Speed (u/s) to have a shot lerp with shotSpeedStraightEnd if the shot ends prematurely with a surface hit. This prevents the shot flying off at super high speeds.")]
            [SerializeField]
            float shotSpeedMaxSurface;
            [Tooltip("Time (s) a StraightShot should last before becoming normal.")]
            [SerializeField]
            float shotStraightTime;
            [Tooltip("Speed (u/s) to have a shot arrive at by the end of shotStraightTime.")]
            [SerializeField]
            float shotSpeedStraightEnd;
            [Tooltip("Used to determine how much speed to \"absorb\" upon hitting a surface.")]
            [SerializeField]
            AnimationCurve surfaceAbsorbtionCurve;

        [Header("Tweening")]
            [Tooltip("Time (s) to tween sack from->to its destination. Used to tween to a shot and jump initial position.")]
            [SerializeField]
            float tweenInitTime;
            [Tooltip("Used to stylize a tween. Evaulted via tweenInitTime")]
            [SerializeField]
            public AnimationCurve tweenInitCurve;
    #endregion

    #region PUBLIC METHODS

    public State GetState(){
        return state;
    }

    public bool SackJump(){
        //Can only jump when in SurfaceHit
        if(hitstopping || state != State.Physical || physicalModifier != PhysicalModifier.SurfaceHit) return false;
    
        _tweenInit.Constructor(ref _rb, HackySacker4.vpBC_CollisionAware, tweenInitTime, tweenInitCurve);
        state = State.Tweening;
        StartCoroutine(SackJumpCoroutine1());

        return true;
    }

    //Tween to use init position, then "activate" the physical use, match to player's velocity
    IEnumerator SackJumpCoroutine1(){

        //Tweening to launch initial position...
        while(_tweenInit.Running()){
            yield return null;
        }
        rbPos = _rb.position;
            
        //Add trauma to player's camera to emulate a kick
        CoreData.Instance.Player.hbMgr.AddTramua(0.6f);

        //Unpossess and apply instantaneous launch velocity
        Physical();

        //Get a gravity ratio so matched velocity works irrespective of diff gravities
        float gRatio = normalGravity.magnitude / CoreData.Instance.HSPlayer.GetGravityVector().magnitude; 
        //Match sack's velocity to player's so they can access it
        desiredVelocity = CoreData.Instance.HSPlayer.GetVelocity();
        desiredVelocity.y *= gRatio;
        physicalStats.surfaceHitCounter = 0;
        physicalStats.justShot = false;
    }

    private bool notifiedOf_ShootInputReleased;
    public NotifyShootInputReleased Use(float startTime_inputHeld, HackySacker4.NotifyShotApplied OnNotifyShotApplied){
        //Can't use if histopping, or already being used, or reacting
        if(hitstopping || state == State.Tweening || state == State.Reacting) return null;

        notifiedOf_ShootInputReleased = false;
        
        //Construct new tween and begin (constructor starts coroutine)
        float startTime = Time.time;
        state = State.Tweening;
        StartCoroutine(UseCoroutine1(_r.transform.position, startTime_inputHeld, OnNotifyShotApplied));

        //When the shoot input is released, player will call this delegate to notify me
        return OnNotifyShootInputReleased;
    }

    //A delegate called by player once the shoot input has been released
    public void OnNotifyShootInputReleased(){
        notifiedOf_ShootInputReleased = true;
    }

    //Tween to use init position, then "activate" the default use, launch
    IEnumerator UseCoroutine1(Vector3 rendererPosInit, float startTime_inputHeld, HackySacker4.NotifyShotApplied OnNotifyShotApplied){
        float elapsed = -1f;

        _r.transform.parent.SetParent(CoreData.Instance.PlayerCamera.transform, true);

        //Continue to wait for input to be released or shotHoldSeconds to elapse, and align position still
        while(!notifiedOf_ShootInputReleased && (Time.time - startTime_inputHeld) < shotHoldSeconds){
            Vector3 collisionFixedPos = _r.transform.parent.position;
            CoreData.Instance.HSPlayer.FixViewportPointCollision(ref collisionFixedPos);
            _r.transform.parent.position = collisionFixedPos;
            yield return null;
        }

        _rb.position = HackySacker4.vpBC_CollisionAware.position;
        rbPos = _rb.position;
        _r.transform.parent.SetParent(this.transform, true);
        _r.transform.parent.rotation = Quaternion.identity; //Temp fix for squasher

        //At this point, we have elapsed shotHoldSeconds, doesn't matter if they released it. Set if necessary
        if(elapsed == -1f) elapsed = Mathf.Min(shotHoldSeconds, Time.time - startTime_inputHeld);
        shotHoldPct = elapsed / shotHoldSeconds;

        //Evaluate shot type before hitstop, so that if the player changes their state between hitstop, they get the state when they first clicked shoot
        Vector3 shotPartial = EvaluateShot();
            
        //Add trauma to player's camera to emulate a kick
        CoreData.Instance.Player.hbMgr.AddTramua(0.6f);

        //Unpossess and apply instantaneous launch velocity
        Physical();
        
        //Applies the evaluated shot from before the hitstop, when the player actually pressed the button
        ApplyShot(shotPartial);
        _as.Play();
        //Let player know the shot was applied
        OnNotifyShotApplied.Invoke();
        _tweenShoot.Constructor(_r.transform.parent, this.transform, tweenInitTime, tweenInitCurve);
    }

    //Evaluates what type of shot to perform, sets according modifiers, and returns a portion of the calculation
    Vector3 EvaluateShot(){
        //Starting at the bottom of the screen, angle shot upward to veer it toward center
        physicalModifier = PhysicalModifier.StraightShot;
        shotSpeedInit = Mathf.Lerp(shotSpeedMin, shotSpeedMax, shotHoldPct);
        float shotAngleInit = Mathf.Lerp(shotAngle_SpeedMin, shotAngle_SpeedMax, shotHoldPct);
        return Quaternion.Euler(shotAngleInit * -1f, 0f, 0f) * Vector3.forward * shotSpeedInit;
    }

    //Applies the shot velocity with the partial calcualted before the hitstop, as well as any associated coroutines of physicalModifier
    void ApplyShot(Vector3 shotPartial){
        //Begin timer if necessary
        if(physicalModifier == PhysicalModifier.StraightShot){
            StartCoroutine(StraightShotCoroutine());
        }
        //Full equation:  CoreData.Instance.PlayerCamera.transform.rotation * Quaternion.Euler(shotAngle, 0f, 0f) * Vector3.forward *  shotSpeedInit;
        desiredVelocity = CoreData.Instance.PlayerCamera.transform.rotation * shotPartial;
        shotPosInit = rbPos;
        physicalStats.surfaceHitCounter = 0;
        physicalStats.justShot = true;
    }

    //Wait for shotStraightTime to elapse, then disable StraightShot modifier
    IEnumerator StraightShotCoroutine(){
        float elapsed = 0;
        shotStraightPct = 0;
        while(elapsed < shotStraightTime){
            elapsed += Time.deltaTime;
            shotStraightPct = elapsed / shotStraightTime;

            if(physicalModifier != PhysicalModifier.StraightShot) break;
            yield return null;
        }

        //Timer over, reset physicalModifier if state wasn't interrupted
        if(physicalModifier == PhysicalModifier.StraightShot){
            physicalModifier = PhysicalModifier.None;
            desiredVelocity = desiredVelocity.normalized * Mathf.Clamp(desiredVelocity.magnitude, 0f, shotSpeedStraightEnd);
        }
    }
    
    //Fires a ray from a ViewportPoint toward a plane at point facing the viewport, and gives the distance traveled
    //Used to calculate a certain height above the sack, determined by a viewport position
    bool ViewportPointTowardPlane_Magnitude(float u, float v, Vector3 planeOrigin, ref Ray toward, ref float mag){
        //Make a plane representing the "ceiling" of the current view, find how far upward the sack can travel to reach it
        Vector2 vpp = new Vector2(u, v);
        toward = CoreData.Instance.PlayerCamera.ViewportPointToRay(vpp);

        //Create a plane at the vpp with the world Y-up
        Vector3 planevpr = toward.direction * -1f; planevpr.y = 0;
        Plane plane = new Plane(planevpr, planeOrigin);

        //Get the distance of the viewport ray to the plane, this is how far the ray must travel to align with the sack's XZ position
        return plane.Raycast(toward, out mag);
    }

    public bool Bunt(){
        //A bunting HackySack is either physicaled, juggling, or holding
        if(!hitstopping && state != State.Physical) return false;
        physicalModifier = PhysicalModifier.Bunt;
        
        //Get sack's viewport position
        Vector3 vpp = CoreData.Instance.PlayerCamera.WorldToViewportPoint(_rb.position);


        //Make a plane representing the "ceiling" of the current view, find how far upward the sack can travel to reach it
        Ray towardPoint = new Ray();
        float mag = 0f;

        
        //The height is how high we will allow the sack to reach in its bunt, so it wont go out of view
        float buntMaxHeight = CoreData.Instance.PlayerCamera.transform.position.y + 1.5f;

        //buntMinHeight clamps a non-trickFeel bunt to be above the current height so that the BalTraj equation doesn't error...
        //..whole buntMinHeight_trickFeel clamps a trickFeel bunt to be comfortable at a minimum, but itself is clamped if it would be higher than the ceilingV
        float buntMinHeight = _rb.position.y + .1f;
        float buntMinHeight_trickFeel = _rb.position.y + 1f;

        ViewportPointTowardPlane_Magnitude(vpp.x, ceilingV, rbPos, ref towardPoint, ref mag);
        float playerPitch = -1*SignifyAngle(CoreData.Instance.HSPlayer.pitchPivot.localEulerAngles.x);

        //If the magnitude to the "ceiling" is negative, and the player is looking up, the raycast overflowed and max height should be kept to maximum
        //...Otherwise, go to that intersection and find the Y distance desired
        if(!(mag <= 0 && (-1*SignifyAngle(CoreData.Instance.HSPlayer.pitchPivot.localEulerAngles.x)) >= 0)){
            Vector3 hitPoint = towardPoint.origin + towardPoint.direction * mag;
            //Bunt can be no lower than the current sack height + buffer, and no higher than the player's head + 5f
            buntMaxHeight = Mathf.Clamp(hitPoint.y, buntMinHeight, buntMaxHeight);
            //If the ceilingV height is above the trickFeel minimum height, use the ceilingV one so it doesn't go out of screen
            if(hitPoint.y < buntMinHeight_trickFeel) buntMinHeight_trickFeel = hitPoint.y;
        }

        //The sack normally wishes to go buntModifier units in 1 second, meaning all normal bunts will last one second
        float latSpeedDesired = buntModifier;
        float latDistDesired = buntModifier;

        //trickFeelRatio = the ratio of lateral speed to the player's max speed
        //But, if the modifier is below a % of the movement speed (based on trickfeelThreshold), make bunts last less time, by increasing lateral speed
        //...and with the ballistics calculations, will return a higher gravity to match
        if(trickFeelRatio <= trickfeelThreshold){
            buntMaxHeight = Mathf.Lerp(buntMinHeight_trickFeel, buntMaxHeight, (trickFeelRatio / trickfeelThreshold));
            latSpeedDesired = Mathf.Lerp(latSpeedDesired, latSpeedDesired * 1.5f,  1 - (trickFeelRatio / trickfeelThreshold));
        }

        //Get current input to treat as a direction for the bunt
        Vector2 _input = CoreData.Instance.HSPlayer.GetMovementInput().normalized;
        Vector3 input = CoreData.Instance.Player.GetYawPivot().rotation * new Vector3(_input.x, 0f, _input.y);
        
        //With no input, cancel existing velocity and make the sack go up
        if(input.magnitude == 0){
            desiredVelocity = Bounce(buntMaxHeight, Vector3.zero);
            goto setResult;
        } else{
            bunt_lastTimeBuntedWithInput = Time.time;
        }

        //With input...
        //Solve the ballistic arc necessary to go that distance, at that speed, at the bunt's max height we calculated earlier
        Vector3 vOut;
        float gOut;
        // SolveBalArcLat_EnforceRatio(_rb.position, _rb.position + input * buntModifier, buntModifier, 1f, buntMaxHeight, out vOut, out gOut);
        BallisticTrajectory.solve_ballistic_arc_lateral(_rb.position, latSpeedDesired, _rb.position + input * latDistDesired, buntMaxHeight, out vOut, out gOut);
        buntGravity = Vector3.down * gOut;
        desiredVelocity = vOut;

        //NOTE: This is a fairly arbitrary process of reduction, tweaked based on what felt good, could use simplification if thought about further
        //If the ratio is too little, the sack would look oddly slow, consider this a bad and penalize
        //This means the player is trying to juggle a sack too close to the top of its arc
        float g_to_l = gOut / buntModifier; //Gravity Mag: Lateral Mag
        float min_g_to_l = ratio_Bunt_G_To_L_min;                       //Minium ratio allowed
        if(g_to_l < min_g_to_l){
            //To penalize, reduce the bunt modifier (which reduces lateral speed and lateral distance)...
            float reducedBuntModifier = buntModifier * (g_to_l / min_g_to_l);

            //...and use it to recalculate the arc...
            BallisticTrajectory.solve_ballistic_arc_lateral(_rb.position, reducedBuntModifier, _rb.position + input * reducedBuntModifier, buntMaxHeight, out vOut, out gOut);
            
            //...and then increase gravity exponentially to give a >1 ratio
            float minG = buntGravityMin.magnitude; //minimum gravity
            float maxMult = 6f; //maximum multiplication of gravity
            float penalty = Mathf.Pow((g_to_l / min_g_to_l), 4); //penalty of multiplication due to ratio value
            float newOutGravityMag = gOut * ( maxMult - (maxMult * penalty) ); //increase gravity by up to maxMult times
            newOutGravityMag = Mathf.Max(newOutGravityMag, minG); //Clamp gravity above 1minG
            buntGravity = Vector3.down * newOutGravityMag;
            desiredVelocity = vOut;
        } else{
            //Even if the ratio is acceptable, it may be below the minimum desired gravity, because ratio isn't 1:1 with the magnitude of gravity
            //So, arbitrarily increase it if necessary. This WILL decrease how far it travels, but based on a good ratio, shouldn't be noticeable
            if(enforceBuntGravityMin && gOut < buntGravityMin.magnitude){
                gOut = buntGravityMin.magnitude;
                buntGravity = Vector3.down * gOut;
            }
        }

        //Set physicalModifier and hitstop
        setResult:
        rendererRotV -= desiredVelocity;
        physicalStats.surfaceHitCounter = 0;
        CoreData.Instance.Player.hbMgr.AddTramua(0.6f);
        Physical();
        StartCoroutine(Hitstop(hitstopLengthJuggle));
        return true;
    }

    Vector3 Bounce(float maxHeight, Vector3 lateralVelocity){
        Vector3 gravity = Vector3.zero;
        switch(physicalModifier){
            case PhysicalModifier.Bunt:
                buntGravity = buntGravityMin;
                gravity = buntGravity;
                break;
            case PhysicalModifier.SurfaceHit:
            case PhysicalModifier.None:
                gravity = normalGravity;
                break;
            default:
                break;
        }

        return lateralVelocity + Vector3.up * Mathf.Sqrt(2 * gravity.magnitude * (maxHeight - _rb.position.y));
    }

    float SignifyAngle(float angle) {
        while (angle > 180f)
            angle -= 360f;
        while (angle <= -180f)
            angle += 360f;
        return angle;
    }

    void UpdateBuntModifier(){
        //Get the current vertical look angle of the player
        float angle = CoreData.Instance.HSPlayer.pitchPivot.localEulerAngles.x;
        angle = -1*SignifyAngle(angle);

        //Used to determine the sack lateral speed, sack lateral distance, and player speed during a bunt
        buntModifierNormalized = buntCurve.Evaluate(angle);
        buntModifier =           buntCurve.Evaluate(angle) * buntModifierScale;
        trickFeelRatio =         Mathf.Clamp01(buntModifier / CoreData.Instance.Player.maxWalkSpeed);
    }    
    #endregion
    #region METHODS
    
    public bool Physical(){
        state = State.Physical;
        return true;
    }

    public bool React(){
        // state = State.Reacting;
        return true;
    }

    void ResetRbVars(){
        rbPos = Vector3.zero;
    }
    
    //Hitstop coroutine, waits hitstopLength and then sets `hitstopping` false, which is checked to prevent UpdateSack() from performing certain states
    IEnumerator Hitstop(float hitstopLength){
        //Play sound and squash sack
        _as.Play();
        _r.transform.parent.localScale = new Vector3(1f, 0.4f, 1f);

        //Perform actual hitstop wait
        yield return new WaitForSeconds(hitstopLength);
        hitstopping = false;

        yield return new WaitForSeconds(0.1f);
        _r.transform.parent.localScale = Vector3.one;
    }

    void UpdateSack(){
        //Only perform collision raycast if physical or reacting
        if(state != State.Physical && state != State.Reacting){
            switch(state){
                case State.Tweening:
                    OnStateTweening();
                    break;
                default:
                    break;
            }
        } else{
            //Fire raycast to check for a hit, being .1f past amount it will move for SLIGHT protection, but if fast enough, it WILL still go through a wall (FIX NEEDED)
            RaycastHit rHit;

            Physics.Raycast(_rb.position, desiredVelocity, out rHit, desiredVelocity.magnitude*Time.fixedDeltaTime + 0.1f, (int)L.SurfaceLayer | (int)L.FoeLayer);

            //If hit something...
            if(rHit.collider != null){
                switch(1 << rHit.collider.gameObject.layer){
                    case (int) L.SurfaceLayer: 
                        OnSurfaceHit(rHit);
                        break;
                    case (int) L.FoeLayer:
                        OnFoeHit(rHit, rHit.collider.gameObject.GetComponent<Foe>());
                        break;
                }
                _as.Play();
            }

            //Don't perform below updates if in hitstop (should only be true here in the first frame of a hit)
            if(hitstopping) return;

            if(state == State.Reacting) OnStateReacting();
            else OnStatePhysical();
        }
    }

    void OnStatePhysical(){
        switch(physicalModifier){
            case PhysicalModifier.StraightShot:
                if((shotPosInit - rbPos).magnitude > Mathf.Lerp(shotEffectiveRangeMin, shotEffectiveRangeMax, shotHoldPct)){
                    if(desiredVelocity.magnitude > shotSpeedStraightEnd)
                        desiredVelocity = desiredVelocity.normalized * shotSpeedStraightEnd;
                    physicalModifier = PhysicalModifier.None;
                }
                break;
            case PhysicalModifier.Bunt:
                ApplyBuntMagnetism();
                desiredVelocity += buntGravity*Time.fixedDeltaTime;

                //If this sack was just bunted, (within bunt_lastTimeBuntedWithInput seconds), 
                //...AND the player isn't inputting, 
                //..reduce velocity so it doesn't go flying offscreen, reducing less the more you are in trickFeel (cubic)
                if(CoreData.Instance.HSPlayer.GetMovementInput().magnitude == 0 &&
                (Time.time - bunt_lastTimeBuntedWithInput) <= justBuntedNormalWithInputWindow &&
                (bunt_lastTimeBuntedWithInput != -1f)){
                    desiredVelocity.x *= Mathf.Lerp(1f, .1f * Time.fixedDeltaTime, Mathf.Pow(trickFeelRatio, 3));
                    desiredVelocity.z *= Mathf.Lerp(1f, .1f * Time.fixedDeltaTime, Mathf.Pow(trickFeelRatio, 3));
                }
                break;
            case PhysicalModifier.FloorBunt:
                desiredVelocity += buntGravity*Time.fixedDeltaTime;
                break;
            case PhysicalModifier.Bounceback:
                desiredVelocity += bouncebackGravity*Time.fixedDeltaTime;
                break;
            case PhysicalModifier.SurfaceHit:
            case PhysicalModifier.None:
                //if horizontal velocity is above a lob shot's max speed, that means it WAS a straight shot and is over, so it needs to be clamped down

                //Apply gravity
                desiredVelocity += normalGravity*Time.fixedDeltaTime;
                break;
        }

        //Apply velocity to HackySack
        rbPos += desiredVelocity*Time.fixedDeltaTime;

        //Tell rb to update position
        _rb.MovePosition(rbPos);
    }

    //These clamping and reduction functions will maintain the angle, but may reduce magnitude below the clampTo
    //This is because we are clamping separately, so which changes the angle, which would have correct separate magnitude...
    //... but to maintain angle, which acts more familiar, we use the normalized direction to apply the clamped COMBINED magnitude
    //Hopefully isn't very noticeable
    void ClampLatAndHorzSeparately(ref Vector3 v, float clampTo){
        Vector3 normalized = v.normalized;
        //Clamp terminal lateral and vertical velocity
        if(Mathf.Abs(v.y) > clampTo){ 
            v.y = Mathf.Sign(v.y) * clampTo;
        }
        Vector3 v_lateral = new Vector3(v.x, 0f, v.z);
        if(v_lateral.magnitude > clampTo){
            v = v_lateral.normalized*clampTo + Vector3.up*v.y;
        }

        v = normalized * v.magnitude;
    }

    void ReduceLatAndHorzSeparately(ref Vector3 v, float clampTo, float pctLeftPerFrame){
        Vector3 normalized = v.normalized;

        //Clamp terminal lateral and vertical velocity
        if(Mathf.Abs(v.y) > clampTo){
            v.y *= pctLeftPerFrame;
        }

        Vector3 v_lateral = new Vector3(v.x, 0f, v.z);
        if(v_lateral.magnitude > clampTo){
            v_lateral *= pctLeftPerFrame;
            v.x = v_lateral.x; v.z = v_lateral.z;
        }
        v = normalized * v.magnitude;
    }
    
    //Lerp the sack toward a foot position in the viewport
    void ApplyBuntMagnetism(){
        //get viewport position for L or R foot, and override its Y pos to the player's Y
        Vector3 vppPos = HackySacker4.ClosestFoot(rbPos).position;
        CoreData.Instance.HSPlayer.FixViewportPointLR(ref vppPos);
        CoreData.Instance.HSPlayer.FixViewportPointCollision(ref vppPos);
        
        //Align the vpPos Y with the sack's Y to make lerp lateral only
        vppPos.y = rbPos.y;
        rbPos = Vector3.Lerp(rbPos, vppPos, Time.fixedDeltaTime);
    }

    void OnStateTweening(){
        //Do nothing while tweening, state coroutines handle tweening
    }

    void OnStateReacting(){
        OnStatePhysical(); //does nothing rn, to be overwritten by custom HackySacks
    }

    //OneFoeHit() causes a reaction to a target, and launches HackySack back toward player
    void OnFoeHit(RaycastHit rHit, Foe f){

        //if the target is reacting, its not valid, treat it as a surface
        if(f.IsReacting()){
            OnSurfaceHit(rHit);
            return;
        }

        //Hitstop
        hitstopping = true;
        StartCoroutine(Hitstop(hitstopLengthHit));

        Bounceback(rHit.point);

        //Make target and HackySack react
        f.React(this);
    }

    //Reorient desiredVelocity to arc back to vpBC
    void Bounceback(Vector3 hitPoint){

        //Get necessary variables to calculate ballistic trajectory for Sack to reach Player
        Vector3 footPos = HackySacker4.ClosestFoot(rbPos).position;

        CoreData.Instance.HSPlayer.FixViewportPointLR(ref footPos);
        Vector3 dir_ToFoot = (footPos - hitPoint); //direction from Sack to Player's closest foot

        //Arbritrarily set maximum height during travel as higher, the starting position or the foot's position plus some padding
        float maxHeight = Mathf.Max(footPos.y + 5f, rbPos.y + 2f);

        //Get the lateral magnitude from sack->player
        Vector3 tmp_footPos = footPos; tmp_footPos.y = rbPos.y;
        Vector3 midpoint = Vector3.Lerp(tmp_footPos, rbPos, 0.5f);

        Ray towardPoint = new Ray();
        float mag = 0f;
        ViewportPointTowardPlane_Magnitude(0.5f, ceilingV, midpoint, ref towardPoint, ref mag);
            
        //Get the magnitude for that ray to intersect the plane, and go to that point
        //...that will be the maximum point of the arc, provide the ballistic arc with that height (clamp the height to something reasonable first)
        maxHeight = (towardPoint.origin + (towardPoint.direction * mag)).y;
        maxHeight = maxHeight > footPos.y + 5f || maxHeight <= footPos.y ? footPos.y + 5f : maxHeight;
        float lateralDistRequired = new Vector2(dir_ToFoot.x,dir_ToFoot.z).magnitude;
        float lateralSpeed = lateralDistRequired;

        Vector3 vOut;
        float gOut;

        SolveBalArcLat_EnforceRatio(rbPos, footPos, ratio_Surface_G_To_L_min, lateralSpeed, maxHeight, out vOut, out gOut);

        desiredVelocity = vOut;
        bouncebackGravity = Vector3.down * gOut;
        physicalModifier = PhysicalModifier.Bounceback;
        Physical();
    }

    bool SolveBalArcLat_EnforceRatio(Vector3 projPos, Vector3 targetPos, float ratio, float lateralSpeed, float maxHeight, out Vector3 fireVel, out float fireGrav){

        BallisticTrajectory.solve_ballistic_arc_lateral(projPos, lateralSpeed, targetPos, maxHeight, out fireVel, out fireGrav);
        //Adjust ratio if lateral is too high
        float g_to_l = fireGrav / lateralSpeed; //Gravity Mag: Lateral Mag
        if(g_to_l < ratio){
            //Decrease lateral speed to get to minimum ratio and recalculate vOut and gOut
            lateralSpeed *= fireGrav / (lateralSpeed * ratio);
            g_to_l = fireGrav / lateralSpeed;
            BallisticTrajectory.solve_ballistic_arc_lateral(projPos, lateralSpeed, targetPos, maxHeight, out fireVel, out fireGrav);
        }

        return true;
    }

    // https://github.com/andywiecko/RotateVectorToLieOnPlane/
    void RotateVectorToLieOnPlane(Vector3 vectorToRotate, Vector3 axisToRotateAround, Vector3 normalOfPlane, ref float theta1, ref float theta2){
            var v = vectorToRotate.normalized;
            var n = normalOfPlane.normalized;
            var r = axisToRotateAround.normalized;


            var A = Vector3.Dot(v, n);
            var B = Vector3.Dot(Vector3.Cross(r, v), n);
            var C = Vector3.Dot(v, r) * Vector3.Dot(n, r);

            var tmp1 = C - A;
            var tmp2 = B * B;
            var tmp3 = tmp1 * tmp1 + tmp2;
            var delta = Mathf.Sqrt(tmp2 * (tmp3 - C * C));

            var x = ((C * tmp1) - delta) / tmp3;
            var y = tmp1 * x / B - C / B;
            theta1 = Mathf.Atan2(y, x) * Mathf.Rad2Deg;

            x = ((C * tmp1) + delta) / tmp3;
            y = tmp1 * x / B - C / B;
            theta2 = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
    }

    void OnSurfaceHit(RaycastHit hit){
        if(state != State.Physical)Physical();

        if(physicalModifier == PhysicalModifier.StraightShot){
            if(desiredVelocity.magnitude > shotSpeedMaxSurface)
                desiredVelocity = desiredVelocity.normalized * shotSpeedMaxSurface;
        }
        physicalModifier = PhysicalModifier.SurfaceHit;

        //Reflect velocity off surface
        Vector3 velocityReflected = Vector3.Reflect(desiredVelocity,hit.normal);

        //Default fire values for basic reflection, overriden if ballistic trajectory is possible
        Vector3 fireVel = velocityReflected;
        float fireGrav = normalGravity.magnitude;

        if(Vector3.Angle(Vector3.up,hit.normal) < maxFSA){
            fireVel.x *= floorFrictionLeftover_Lat; fireVel.z *= floorFrictionLeftover_Lat;
            fireVel.y *= floorFrictionLeftover_Vert;
            physicalStats.surfaceHitCounter = 0;
            physicalModifier = PhysicalModifier.None;
            Vector3 bounceVel = Bounce(rbPos.y + 1f, new Vector3(fireVel.x, 0f, fireVel.z));
            if(fireVel.y < bounceVel.y){
                fireVel = bounceVel;
                goto setResultNormal;
            }
        } else{
            float pctAwayFromNormal = Vector3.Angle(fireVel,hit.normal) / 90f;
            fireVel *= surfaceAbsorbtionCurve.Evaluate(pctAwayFromNormal);
        }

    //REFLECTION HELPER (Perform modifications to the reflection to help player out):
    //Does so by:
    //1. Rotate velocity upward in world space to provide more air time to sack

        //Find the lateral right and forward axes, in order to rotate velocity upward in world space
        Vector3 latRefl_Right = Vector3.Cross(new Vector3(velocityReflected.x, 0, velocityReflected.z), Vector3.up);
        Vector3 latRefl_Forward = new Vector3(velocityReflected.x, 0, velocityReflected.z);
        //Get angle from (world space) 0-deg-pitch -> velocity-pitch
        float reflDeg_fromFwd = Vector3.SignedAngle(latRefl_Forward, velocityReflected, latRefl_Right);


        float reduced_reflectAngleMin = Mathf.Lerp(reflDeg_fromFwd, reflectAngleMin, 1f / (physicalStats.surfaceHitCounter+1));
        float makeupDesired_Deg = (reduced_reflectAngleMin) - reflDeg_fromFwd;

        //If our reflection pitch is below our desired minimum, we will attempt to increase it
        if(reflDeg_fromFwd < reduced_reflectAngleMin){

            //Find how much available upward pitch rotation is possible before hitting the surface
            float theta1 = 0f, theta2 = 0f;
            RotateVectorToLieOnPlane(velocityReflected, latRefl_Right, hit.normal, ref theta1, ref theta2);
            float availableMakeup_Deg = theta1 >= 0 ? theta1 : theta2;

            //If we cannot increase reflect angle to our desired minimum, perform basic reflection...
            //...as ballistic trajectory requires upward velocity, and chances are we will just hit the surface again
            if(makeupDesired_Deg > availableMakeup_Deg){
                    makeupDesired_Deg = availableMakeup_Deg;
            }


            //Otherwise, increase pitch until to reach our desired minimum
            fireVel = Quaternion.AngleAxis(makeupDesired_Deg, latRefl_Right) * fireVel;
        }

        setResultNormal:
        // physicalModifier = PhysicalModifier.None;
        desiredVelocity = fireVel;
        //Hitstop
        hitstopping = true;
        StartCoroutine(Hitstop(hitstopLengthHit));
        physicalStats.justShot = false;
        physicalStats.surfaceHitCounter++;
        return;
    }

    bool HandleFloorSurface(ref RaycastHit hit, ref Vector3 velocity){
        //Get the surface's normal's degrees away from World Up
        float degAwayWorldUp = Vector3.Angle(Vector3.up,hit.normal);

        //If hit a floor surface...
        if(degAwayWorldUp < maxFSA){
            Bounce(rbPos.y + 1f, new Vector3(desiredVelocity.x, 0f, desiredVelocity.z) * 0.125f);
            physicalModifier = PhysicalModifier.FloorBunt;
            //Surface is considered a floor
            return true;
        }
        //Surface is not considered a floor
        return false;
    }

    //Jack said sacks rotate backward of where they are kicked
    void UpdateRendererRotV(){
        float scaler = 10f;
        float reduceRotVBy = (Vector3.one * Time.deltaTime).magnitude * scaler / 2f;
        if(rendererRotV.magnitude > reduceRotVBy){
            rendererRotV = rendererRotV.normalized * (rendererRotV.magnitude - reduceRotVBy);
            _r.transform.rotation *= Quaternion.Euler(rendererRotV * Time.deltaTime * scaler * 2f);
        } else{
            rendererRotV *= 0;
        }
    }

    #endregion
    #region MONOBEHAVIORS

    void Reset(){
        //Set property defaults
        
        //m/s
        normalGravity = Vector3.down * 15f;
        straightGravity = Vector3.down * 2.5f;
        buntGravityMin = Vector3.down * 12.5f;        //arbitrary, feels good (i try to get it close to ratio_Bunt_G_To_L_min feeling)
        enforceBuntGravityMin = true;

        //UV unit
        ceilingV = 0.9f;

        //deg
        reflectAngleMin = 20f;
        maxFSA = 45f;
        ratio_Surface_G_To_L_min = 5f;              //arbitrary, feels good
        floorFrictionLeftover_Lat = .75f;               //[0-1]

        //m
        buntModifierScale = 10f;
        trickfeelThreshold = 1f;
        justBuntedNormalWithInputWindow = .1f;       //s
        ratio_Bunt_G_To_L_min = 1.75f;              //arbitrary, feels good
        
        shotHoldSeconds = .2f;                      //s
        shotAngle_SpeedMax = 15f;                            //deg
        shotSpeedMin = 10f;                        //m/s
        shotStraightTime = 0.05f;                       //s
        shotSpeedMax = 40f;                    //m/s
        
        tweenInitTime = 0.1f;                       //s
        
        hitstopLengthHit = 0.00f;                   //s
        hitstopLengthJuggle = 0.00f;                //s
    }

    void Start(){
        //Get components

        //The rigibody is used only for movement interpolation, as the movement/collision is performed in FixedUpdate()
        _rb = GetComponent<Rigidbody>();
        _rb.detectCollisions = false; //Collision is handled via a Raycast in UpdateSack()

        _r = transform.GetComponentInChildren<Renderer>();
        _as = transform.GetComponentInChildren<AudioSource>();
        if(_r == null) Debug.LogError(name + " doesn't have child renderer GameObject.");
        if(_as == null) Debug.LogError(name + " doesn't have child Audio Source GameObject.");

        //Set field defaults
        state = State.Physical;
        rbPos = _rb.position;
        physicalStats = new PhysicalStats();
        //Dynamically generated w/ BalTraj
        buntGravity = normalGravity;
        surfaceHitGravity = normalGravity;

        //Construct tweeners
        gameObject.AddComponent<RbLiveTransformTween>();
        gameObject.AddComponent<TransformLiveTransformTween>();

        _tweenInit = GetComponent<RbLiveTransformTween>();
        _tweenShoot = GetComponent<TransformLiveTransformTween>();
    }
    
    //Perform animation
    void LateUpdate(){
        //Every frame, reduce the accumulated rotational velocity of the renderer
        UpdateRendererRotV();
    }

    //Perform physics
    void FixedUpdate(){
        //Don't peform below updates if in hitstop
        if(hitstopping) return;

        //Wait for scenes to load and references to be stored
        if(!CoreData.referencesValid) return;

        UpdateBuntModifier();
        // UpdatePlayerMaxSpeed();
        UpdateSack();
    }

    static bool showGUI = false;
    static bool lastVal = false;
    void OnGUI(){
        if(Keyboard.current.hKey.isPressed && lastVal != Keyboard.current.hKey.isPressed){
            showGUI = !showGUI;
        }
        lastVal = Keyboard.current.hKey.isPressed;

        //Debug reset HS
        if(Keyboard.current.rKey.isPressed){
            rbPos = CoreData.Instance.PlayerCamera.transform.position + CoreData.Instance.PlayerCamera.transform.forward;
            _rb.position = rbPos;
        }
        if(Keyboard.current.hKey.isPressed)
            showGUI = !showGUI;

        if(!showGUI) return;
        
        Vector2 textRect = new Vector2(500,20);
        GUIStyle style = new GUIStyle();
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = Color.white;
        GUIStyle style2 = new GUIStyle();
        style2.alignment = TextAnchor.LowerLeft;
        style2.normal.textColor = Color.cyan;

        if(!CoreData.referencesValid) return;

        Vector3 hsScreenPos = CoreData.Instance.PlayerCamera.WorldToScreenPoint(_rb.position);
        hsScreenPos.x = hsScreenPos.x - (textRect.x/2);
        hsScreenPos.y = CoreData.Instance.Player.camera.pixelHeight - hsScreenPos.y;

        GUI.Label(new Rect(new Vector2(5, 195), textRect), ("Bunt Lateral Speed: " + (buntCurve.Evaluate(-1*SignifyAngle(CoreData.Instance.HSPlayer.pitchPivot.localEulerAngles.x)) * 10f).ToString("F2")).ToString(), style2);

        if(hsScreenPos.z >= 0) //Prevent drawing if not in frustum
            GUI.Label(new Rect(hsScreenPos, textRect), System.Enum.GetName(typeof(State), state) + "\n" + System.Enum.GetName(typeof(PhysicalModifier), physicalModifier), style);
    }
    #endregion
    #region Tweens
    public abstract class LiveTween : MonoBehaviour{
        public bool running;
        public float time; //seconds
        public float progress;
        public AnimationCurve curve;
        public float eval; //either == progress if (curve == null) or curve.Evaluate(progress)

        //Should be called via the custom constructor
        protected abstract IEnumerator Tween();

        public bool Running(){return running;}
        public float Progress(){return progress;}
    }

    //(Live Tween for Collider, where the start is fixed, but the end is constantly updated)
    public class RbLiveColliderTween : LiveTween {
        Rigidbody rb;
        Vector3 tweenStart;
        Collider target;

        //Not an automatic constructor b/c I have to AddComponent
        //...I have to AddComponent because it extends MonoBehaviour
        //...I have to extend MonoBehaviour because I need to have a Coroutine
        public void Constructor(ref Rigidbody toTween, Collider target, float time, AnimationCurve curve = null){
            running = true;
            this.time = time;
            progress = 0;
            this.curve = curve;

            rb = toTween;
            tweenStart = rb.position;
            this.target = target;
            //Begin coroutine
            StartCoroutine(Tween());
        }

        protected override IEnumerator Tween()
        {
            while(progress != 1){
                //Get eval from curve if (curve != null)
                eval = curve == null ? progress : curve.Evaluate(progress);
                //Move rb, overshoot allowed if curve produces it
                rb.MovePosition(Vector3.LerpUnclamped(tweenStart, target.bounds.center,eval));
                //Update progress
                progress = Mathf.Clamp01(progress + (1/time) * Time.deltaTime);
                yield return null;
            }
            //Finished tween
            running = false;
        }
    }

    //(Live Tween for Transform, where the start is fixed, but the end is constantly updated)
    public class RbLiveTransformTween : LiveTween {
        Rigidbody rb;
        Vector3 tweenStart;
        Transform target;

        //Not an automatic constructor b/c I have to AddComponent
        //...I have to AddComponent because it extends MonoBehaviour
        //...I have to extend MonoBehaviour because I need to have a Coroutine
        public void Constructor(ref Rigidbody toTween, Transform target, float time, AnimationCurve curve = null){
            running = true;
            this.time = time;
            progress = 0;
            this.curve = curve;

            rb = toTween;
            tweenStart = rb.position;
            this.target = target;
            //Begin coroutine
            StartCoroutine(Tween());
        }

        protected override IEnumerator Tween()
        {
            while(progress != 1){
                //Get eval from curve if (curve != null)
                eval = curve == null ? progress : curve.Evaluate(progress);
                //Move rb, overshoot allowed if curve produces it
                rb.MovePosition(Vector3.LerpUnclamped(tweenStart, target.position,eval));
                //Update progress
                progress = Mathf.Clamp01(progress + (1/time) * Time.deltaTime);
                yield return null;
            }
            //Finished tween
            running = false;
        }
    }

    //(Live Tween for Transform, where the start is fixed, but the end is constantly updated)
    public class TransformLiveTransformTween : LiveTween {
        Transform trans;
        Vector3 tweenStart;
        Transform target;

        //Not an automatic constructor b/c I have to AddComponent
        //...I have to AddComponent because it extends MonoBehaviour
        //...I have to extend MonoBehaviour because I need to have a Coroutine
        public void Constructor(Transform toTween, Transform target, float time, AnimationCurve curve = null){
            running = true;
            this.time = time;
            progress = 0;
            this.curve = curve;

            trans = toTween;
            tweenStart = trans.position;
            this.target = target;
            //Begin coroutine
            StartCoroutine(Tween());
        }

        protected override IEnumerator Tween()
        {
            while(progress != 1){
                //Get eval from curve if (curve != null)
                eval = curve == null ? progress : curve.Evaluate(progress);
                //Move rb, overshoot allowed if curve produces it
                trans.position = Vector3.LerpUnclamped(tweenStart, target.position,eval);
                //Update progress
                progress = Mathf.Clamp01(progress + (1/time) * Time.deltaTime);
                yield return null;
            }
            //Finished tween
            running = false;
        }
    }
    #endregion
}
}
