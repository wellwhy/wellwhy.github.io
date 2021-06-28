using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ECM2.Common;

public class MovementStaging : MonoBehaviour
{
    [SerializeField]
    //sbr = stage bar width
    float uiWidth = 0.5f;
    RectTransform parentCanvas;
    RectTransform velocityCursorRectTransform;
    RectTransform velocityCursorBGRectTransform;
    Text velocity_number_text;
    Text sprint_text;
    Vector2 canvasWidthHeight;

    public Transform StepCircle;
    

    float cursor_start_x;
    void Start()
    {
        parentCanvas = GetComponentInParent<Canvas>().GetComponent<RectTransform>();
        canvasWidthHeight = new Vector2(parentCanvas.rect.width, parentCanvas.rect.height);

        makeVelocityCursorBG();
        makeVelocityCursor();
        setupDebugTexts();

        moveMode = walk;
        moveMode_sATE = walk_sATE;
    }

    void makeVelocityCursor(){
        GameObject velocity_cursor = new GameObject();
        velocity_cursor.name = "Velocity Cursor";
        Image set_color_cursor = velocity_cursor.AddComponent<Image>();
        set_color_cursor.color = new Color(0,0.75f,0);
        velocity_cursor.transform.SetParent(this.transform);

        velocityCursorRectTransform = velocity_cursor.GetComponent<RectTransform>();
        velocityCursorRectTransform.pivot = new Vector2(0.0f,0.0f);

        velocityCursorRectTransform.anchoredPosition = 
            new Vector2(-1*canvasWidthHeight.x*uiWidth/2,
                        -1*canvasWidthHeight.y*0.475f); //0.4 just for height, to touch bottom, its not sbr
        cursor_start_x = velocityCursorRectTransform.anchoredPosition.x;

        velocityCursorRectTransform.sizeDelta = new Vector2(canvasWidthHeight.x*.0025f,canvasWidthHeight.x*.025f);
    }

    void makeVelocityCursorBG(){
        GameObject velocity_cursor_bg = new GameObject();
        velocity_cursor_bg.name = "Velocity Cursor BG";
        Image set_color_cursor = velocity_cursor_bg.AddComponent<Image>();
        set_color_cursor.color = new Color(0,0,0);
        velocity_cursor_bg.transform.SetParent(this.transform);

        velocityCursorBGRectTransform = velocity_cursor_bg.GetComponent<RectTransform>();
        velocityCursorBGRectTransform.pivot = new Vector2(0.0f,0.0f);

        velocityCursorBGRectTransform.anchoredPosition = 
            new Vector2(-1*canvasWidthHeight.x*uiWidth/2,
                        -1*canvasWidthHeight.y*0.475f); //0.4 just for height, to touch bottom, its not sbr

        velocityCursorBGRectTransform.sizeDelta = new Vector2(canvasWidthHeight.x*uiWidth,canvasWidthHeight.x*.025f);
    }

    void setupDebugTexts(){
        Text[] texts = this.GetComponentsInChildren<Text>();
        sprint_text = texts[0];
        velocity_number_text = texts[1];
        velocity_number_text.color = new Color(0,0.75f,0);
        velocity_number_text.text = speed.ToString();
    }

    void updateDebugUI(){
        //debugging UI, with cursor being the velocity

        distance = Mathf.Clamp(distance + speed/1000f, 0, 1); //end of bar

        velocityCursorRectTransform.anchoredPosition =
            new Vector2(cursor_start_x + (speed/speedLimit)*canvasWidthHeight.x*uiWidth,
                        velocityCursorRectTransform.anchoredPosition.y);

        velocity_number_text.text = speed.ToString(); 
        sprint_text.text = sprinting ? "SPRINT" : "";             
    }

    // Update is called once per frame
    float eval = 0;
    public float speed = 0;
    float distance = 0;

    bool acc = false;
    bool dec = true;

    const float WALK_SPEED_LIMIT = 1.4f;
    const float JOG_SPEED_LIMIT = 3.0f;
    float speedLimit = WALK_SPEED_LIMIT;

    AccelerationStages walk = new AccelerationStages(0.05f,10f,0.05f,
                                                     10f,0.055f,0.95f);
    AccelerationStages walk_sATE = new AccelerationStages(32f,32f,0.00f,
                                                     32f,32f,0.00f);
    AccelerationStages jog = new AccelerationStages(0.05f,12f,0.05f,
                                                    16f,0.055f,0.95f);
    AccelerationStages jog_sATE = new AccelerationStages(32f,32f,0.00f,
                                                     32f,32f,0.00f);
    AccelerationStages moveMode; //set to walk in start()
    AccelerationStages moveMode_sATE; //set to walk in start()
    float moveModeLerp = 0.0f; //walk
    float moveModeLerpStep = 0.1f;

    const float SPRINT_SPEED_LIMIT = 5.0f;

    public Vector3 currentRawWASDInput = Vector3.zero;
    public Vector3 lastValidRawWASDInput = Vector3.zero;

    public Vector3 lastValidSmoothedCombinedInput = Vector3.zero;

    public Vector3 smoothedKeyboardInput = Vector3.zero;
        public bool noInputOrVelocity = false;
    float smoothAngleThreshold = 90.0f;
        bool smoothAngleThresholdExceeded = false;
        bool smoothAngleThresholdAccel = false;
        float smoothMaximumSpeed = 0.1f; //meters/sec
        float smoothClampThreshold = 0.5f; //degrees

    public bool sprinting = false;
    public float sprintDiagonalReduction = 0.6f; //sprinting diagonally goes from (45) to (45 * ??)

    //ECM2 does FixedUpdate() -> callback -> OnMove() -> Walking() -> me!
    //For now, Update() is smoother than FixedUpdate() b/c FixedUpdate uses physics steps not framerate

    void Update()
    {

        //Store whether or not there was an input this frame, determining acceleration or decceleration
        //currentInput is updated by jake every FixedUpdate() frame ECM2 checks for it

        bool noInput = currentRawWASDInput.isZero();

        //Temporary Keys to switch movement modes, don't allow walk/jog UP/DOWN when sprinting, (there MAY be an issue if Shift and mouse wheel are on the EXACT same frame?)

        if(Mouse.current.scroll.ReadValue().normalized == new Vector2(0,1.0f) && !sprinting)
            switchMoveMode("UP");
        
        if(Mouse.current.scroll.ReadValue().normalized == new Vector2(0,-1.0f) && !sprinting)
            switchMoveMode("DOWN");

        

        //if there is no input (the player wants to stop)
        //or something like a mode switch to a lower speedLimit moveMode occurred (speed goes over max)
        //or the player is inputting too high of an angle of an input switch, so instead of rotating to the new direction...
        //we want to deccelerate
        if(noInput || speed > speedLimit || smoothAngleThresholdExceeded){

            //On the first frame of decceleration, acc is true, and values must be reset or switched

            if(acc){
                eval = 0.0f;
                acc = false;
                dec = true;
            }

            //WAY #1 TO STOP SPRINTING: the player must be sprinting, and they let go of all input or something else that causes them to deccelerate

            if(sprinting)
                sprinting = false;

            //if smoothAngleThresholdAccel is true, the angle was exceeded, player was decellerated, and they finished accelerating fast
            //this returns decelleration to the regular moveMode (instead of sATE)

            if(smoothAngleThresholdAccel){
                smoothAngleThresholdAccel = false;
                Debug.Log("Fast accel DISABLED");
            }

            //to deccelerate, speed is decreased by the decceleration rate, which is evaluated
            //it is clamped below so it never goes below zero
            //but unclamped above because decceleration from a speed greater than speedLimit will be required when switching moveMode from jog -> walk
            //if smooth angle threshold is exceeded, we want to decellerate smoothly, so a different AccelerationStages is used
            if(smoothAngleThresholdExceeded)
                speed -= moveMode_sATE.decAt(eval) * Time.deltaTime;
            else
                speed -= moveMode.decAt(eval) * Time.deltaTime;
            // if(speed < 0.0f)
            //     Debug.Log(speed);
            speed = Mathf.Clamp(speed,0.00f,Mathf.Infinity);
        }

        //else, there is input and speed isn't too high and input angle isn't too high
        //so we want to accelerate

        else if (!noInput){
            //On the first frame of acceleration, dec is true, and values must be reset or switched

            if(dec){
                eval = 0.0f;
                acc = true;
                dec = false;
            }

            //To sprint, the player MUST be accelerating (pressing an input), and that input must be 45 degrees from forward

            if(Keyboard.current.shiftKey.wasPressedThisFrame && !sprinting && vectorIsWithinAngleFromForward(currentRawWASDInput, 45.0f))
                sprinting = true;

            //WAY #2 TO STOP SPRINTING: the player must be sprinting, and their current input must no longer be 45 degrees from forward

            if(sprinting && !vectorIsWithinAngleFromForward(currentRawWASDInput, 45.0f))
                sprinting = false;

            //to accelerate, speed is increased by the acceleration rate, which is evaluated
            //it is clamped below so it never goes below zero
            //it is clamped above, there is no need to allow it to go over
            //if smooth angle threshold is exceeded, we want to accelerate smoothly, so a different AccelerationStages is used

            if(smoothAngleThresholdAccel){
                speed += moveMode_sATE.accAt(eval) * Time.deltaTime;
                Debug.Log("Fast accel enabled");
            }
            else
                speed += moveMode.accAt(eval) * Time.deltaTime;
            speed = Mathf.Clamp(speed,0f,speedLimit);
        } else{
            Debug.Log("I am not accounting for something with input vs. no input.");
        }

        //time always advances, but is clamped
        //this is because either we are deccelerating, and we go from 0 -> 1 and hold at 1 with this clamp
        //or we are accelerating, and we go from 0 -> 1 and hold at 1 with this clamp

        eval += Time.deltaTime;
        eval = Mathf.Clamp(eval,0f,1f);

        updateDebugUI();
    }

    //Smooths WASD keyboard input, if angle is too high to be smoothed well, it sets bools true to force decceleration/acceleration

    public Vector3 smoothKeyboardInput(Vector3 input, Vector3 velocity){

        //If zero is returned by this function, this classes' update will deccelerate

        //NO input, HAS velocity = moving and player wants to decellerate

        if(input.isZero() && velocity != Vector3.zero)
            return Vector3.zero;
        
        //NO input, NO velocity = At a stop
        
        if(input.isZero() && velocity.isZero()){
            noInputOrVelocity = true;
        } 
        
        //HAS input, may or may not have velocity, regardless we want to accelerate

        else{
            
            //If last frame there was no input or velocity, or the sATE and we are at a stop

            if(noInputOrVelocity || (smoothAngleThresholdExceeded && velocity.isZero())){

                //Both cases, we are at a stop, which means we don't want to smooth anything, this resets the process
                //Just set the smoothed keyboard input to the actual input
                //Also, set bools false that determine states as they are no longer true

                smoothedKeyboardInput = input;
                noInputOrVelocity = false;

                //Recheck the sATE (it could be just noInputOrVelocity), and set sATA TRUE to indicate they finished deccelerating
                //And should begin smoothly accelerating

                if(smoothAngleThresholdExceeded && velocity.isZero()){
                    smoothAngleThresholdAccel = true;
                    smoothAngleThresholdExceeded = false;
                }
            }

            //Input changes are smoothed by scaling it by the angle between current input and desired input

            float angle_between_input_and_smoothed = Vector3.Angle(input,smoothedKeyboardInput);

            //If the angle is small enough to be smoothed

            if(angle_between_input_and_smoothed <= smoothAngleThreshold){

                //If the angle at one point was exceeded, but before finishing dec/acc they changed inputs, cancel sATE

                smoothAngleThresholdExceeded = false;

                //The amount to move the smoothed input toward the actual input is based on the angle, a fraction of the threshold + 1 degree
                //There is also a maximum speed the smoothness can go at

                float smooth_factor = Mathf.Clamp(1.0f - (angle_between_input_and_smoothed / (smoothAngleThreshold + 1.0f)),0,smoothMaximumSpeed);
                
                //If the amount to move is super tiny, we will just make the smoothed input == input otherwise it would never converge
                
                if(angle_between_input_and_smoothed <= smoothClampThreshold)
                    smooth_factor = 1.0f;

                //Rotate the smoothed input a little bit closer to the actual input

                smoothedKeyboardInput = Quaternion.Lerp(Quaternion.identity,Quaternion.FromToRotation(smoothedKeyboardInput,input),smooth_factor) * smoothedKeyboardInput;
            }
            
            //Else, the angle is too large to be smoothed, so we will maintain direction and deccelerate, then once hit zero, accelerate with input
            
            else{

                //On the first frame, bool must be set true, and eval reset to allow decceleration from the start of the evaluation

                if(!smoothAngleThresholdExceeded){ //hasnt started yet
                    smoothAngleThresholdExceeded = true;
                    eval = 0.0f;
                }

                //otherwise, nothing happens as it is currently deccelerating, and when it finishes, up above sATA will be set true, smoothed input == input, and it will accelerate
            }
        }

        return smoothedKeyboardInput;
    }
    
    public void switchMoveMode(string direction){
        
        //The previous mode move is used to calculate the new eval position given (I think this barely matters b/c acceleration is mostly constant)

        float previous_move_mode_lerp = moveModeLerp;

        //Either increasing or decreasing the change from WALK -> JOG

        switch(direction){
            case "UP":

                //Move from WALK -> JOG by moveModeLerpStep, from 0.0f to 1.0f

                moveModeLerp = Mathf.Clamp(moveModeLerp + moveModeLerpStep, 0.0f, 1.0f);

                //reevaluate eval and clamp it based on the previous lerp value (I think this barely matters)

                eval = 0.0f;

                break;

            case "DOWN":

                //Move from WALK -> JOG by moveModeLerpStep, from 0.0f to 1.0f

                moveModeLerp = Mathf.Clamp(moveModeLerp - moveModeLerpStep, 0.0f, 1.0f);

                //reset eval because we are starting a new acceleration or decceleration

                eval = 0.0f;

                break;
        }

        //Set moveMode, moveMode_sATE, and speedLimit based on the new lerp value

        moveMode.lerpMe(walk,jog,moveModeLerp);
        moveMode_sATE.lerpMe(walk_sATE,jog_sATE,moveModeLerp);
        speedLimit = Mathf.Lerp(WALK_SPEED_LIMIT,JOG_SPEED_LIMIT,moveModeLerp);
    }

    public class AccelerationStages
    {
        //the starting acceleration
        float acc1;
        
        //the general acceleration
        float acc2;

        //the time to go from starting -> general acceleration (normalized 0-1.0 of time)
        float accSwitchTime;

        //the starting decceleration
        float dec1;

        //the general decceleration
        float dec2;

        //the time to go from starting -> general acceleration (normalized 0-1.0 of time)
        float decSwitchTime;

        //constructor

        public AccelerationStages(float acc1, float acc2, float accSwitchTime, float dec1, float dec2, float decSwitchTime) {
            this.acc1 = acc1;
            this.acc2 = acc2;
            this.accSwitchTime = accSwitchTime;
            this.dec1 = dec1;
            this.dec2 = dec2;
            this.decSwitchTime = decSwitchTime;
        }
        
        //copy constructor

        public AccelerationStages(AccelerationStages copyThis) {
            copyThis.acc1 = acc1;
            copyThis.acc2 = acc2;
            copyThis.accSwitchTime = accSwitchTime;
            copyThis.dec1 = dec1;
            copyThis.dec2 = dec2;
            copyThis.decSwitchTime = decSwitchTime;
        }

        //At time, the acceleration will either be the starting or general acceleration

        public float accAt(float time){
            return time < accSwitchTime ? acc1 : acc2;
        }

        //At time, the decceleration will either be the starting or general decceleration

        public float decAt(float time){
            return time < decSwitchTime ? dec1 : dec2;
        }

        //Linearly lerp each value of 2 different AccelerationStages by amount

        public void lerpMe(AccelerationStages one, AccelerationStages two, float amount){
            this.acc1 = Mathf.Lerp(one.acc1,two.acc1,amount);
            this.acc2 = Mathf.Lerp(one.acc2,two.acc2,amount);
            this.accSwitchTime = Mathf.Lerp(one.accSwitchTime,two.accSwitchTime,amount);
            this.dec1 = Mathf.Lerp(one.dec1,two.dec1,amount);
            this.dec2 = Mathf.Lerp(one.dec2,two.dec2,amount);
            this.decSwitchTime = Mathf.Lerp(one.decSwitchTime,two.decSwitchTime,amount);
        }
    }

    //Gives input to jake, called exactly when input is grabbed in ECM2

    public void giveRawWASDInput(Vector3 input){

        //always store current input, can be none if no input is given this from

        currentRawWASDInput = input;

        //ONLY if input is given, do we update the current direction

        if(!input.isZero())
            lastValidRawWASDInput = input;
    }

    //Gives last valid smoothed mouse+WASD direction to jake, to be used in ECM2 Walking()

    public void giveLastValidSmoothedCombinedInput(Vector3 input){

        //ONLY if input is given, do we update the current last valid smoothed combined input

        if(!input.isZero())
            lastValidSmoothedCombinedInput = input;
    }

    //Checks if vector3 is within WA -> W <- WD

    private bool vectorIsWithinAngleFromForward(Vector3 vector, float angle){
        return Vector3.Angle(Vector3.forward, vector) <= angle;

    }
}