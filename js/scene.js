var startingWidth = window.innerWidth;
var clock = new THREE.Clock();
var scene = new THREE.Scene();
var container = document.getElementById("three_js_container_1");
var camera = new THREE.PerspectiveCamera( 15, container.offsetWidth/container.offsetHeight, 0.1, 1000 );
const renderer = new THREE.WebGLRenderer({container,alpha: true,antialias:false});
renderer.setClearColor( 0x000000, 0 ); 
var loader = new THREE.GLTFLoader();
// var directionalLight = new THREE.DirectionalLight( 0xffffff, 1 );
// directionalLight.position.set(0, 10, 0);
// directionalLight.target.position.set(-5, 0, -10);
// scene.add(directionalLight);
// scene.add(directionalLight.target);
var light = new THREE.HemisphereLight( 0xcccccc, 0xffffff, 1 );
renderer.Out
renderer.gammaOutput = true;
renderer.setPixelRatio( window.devicePixelRatio / 3 );
scene.add( light );

camera.position.z = 2;
var model_1;
var mixer;
var helper;
var skeleton_1;
loader.load( 'loser.glb', function ( gltf ) {
    gltf.scene.traverse( function( node ) {
        if ( node.isMesh ) { 
            node.castShadow = true;
        }
    } );
    model_1 = gltf.scene;
    mixer = new THREE.AnimationMixer( model_1 );
    gltf.animations.forEach( ( clip, i ) => { if(i == 3) mixer.clipAction( clip ).play(); } ); //play animation at index 3
    
    // model_1.position.x = -.15;
    model_1.position.y = -1;
    model_1.position.z = -2;
    // model_1.position.z = -1;
    // model_1.rotation.y = -5.2;
    // model_1.rotation.x = .1;

    scene.add( model_1 ); //ORDER MATTERS HERE
    console.log(model_1);
    //scene.add(helper);
}, undefined, function ( error ) {
	console.error( error );
} );

renderer.setSize(container.offsetWidth,container.offsetHeight);
container.appendChild( renderer.domElement );

// var maxBone = 0;
// var getMaxBone = true;
// var currMaxPosition = new THREE.Vector3();

function animate() {
    requestAnimationFrame( animate );
    var delta = clock.getDelta();
    if ( mixer ) mixer.update( delta );
    renderer.render( scene, camera );
}

window.addEventListener( 'resize', onWindowResize, false );

function onWindowResize(){
    //doesn't go past 21:9 ratio, if you want it to stretch render instead, just change apsect, not renderer.setSize
    console.log(window.innerWidth / window.innerHeight);
    if(window.innerWidth / window.innerHeight > 2.7){
        camera.aspect = window.innerHeight*2.33 / window.innerHeight;
        renderer.setSize( window.innerHeight*2.33, window.innerHeight );
    }
    else{
    camera.aspect = window.innerWidth / window.innerHeight;
    renderer.setSize( window.innerWidth, window.innerHeight );
    }

    camera.updateProjectionMatrix();
}
animate();