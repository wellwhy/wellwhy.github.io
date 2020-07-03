let canvas;
let ctx;
let incr;
let pattern_image;
let pattern;
let pattern_x, pattern_y;
let ran;

window.onload = init;

//The window onload function call.
function init() {
    canvas = document.querySelector("#headerCanvas");
    ctx = canvas.getContext("2d");
    incr = 0;
    window.requestAnimationFrame(canvasLoop);
    pattern_x = 0;
    pattern_y = 0;
    pattern_image = document.querySelector("#pattern");
    pattern = ctx.createPattern(pattern_image, 'repeat');
}

//The running gameloop for the canvas header.
function canvasLoop() {
    incr++;
    if(incr > 9999999) {
        incr = 0;
    }
    drawBackgroundPattern(ctx);
 
    window.requestAnimationFrame(canvasLoop);
}

//Draws the looping background pattern behind the canvas.
function drawBackgroundPattern(c) {
    c.fillStyle = "#444444";
    c.fillRect(0,0,window.innerWidth,window.innerHeight);
    c.fillStyle = pattern;
    c.save();
    c.translate(pattern_x, pattern_y);
    c.fillRect(0,0,window.innerWidth,window.innerHeight);
    c.restore();
    pattern_x+=.75 / 2;
    pattern_y+=.5 / 2;
    if(pattern_x == 75) {
        pattern_x = 0;
        pattern_y = 0;
    }
}