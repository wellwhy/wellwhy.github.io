var startingWidth = window.innerWidth;
var clock = new THREE.Clock();
var scene = new THREE.Scene();
var container = document.getElementById("three_js_container_1");
var camera = new THREE.PerspectiveCamera( 15, container.offsetWidth/container.offsetHeight, 0.1, 1000 );
const renderer = new THREE.WebGLRenderer({container,alpha: true,antialias:true});
renderer.setClearColor( 0x000000, 0 ); 
var loader = new THREE.GLTFLoader();
// var directionalLight = new THREE.DirectionalLight( 0xffffff, 1 );
// directionalLight.position.set(0, 10, 0);
// directionalLight.target.position.set(-5, 0, -10);
// scene.add(directionalLight);
// scene.add(directionalLight.target);
var light = new THREE.HemisphereLight( 0x000000, 0xffffff, 1.5 );
renderer.gammaOutput = true;
scene.add( light );

function vertexShader() {
    return `
      varying vec3 vUv; 
  
      void main() {
        vUv = position; 
        vec4 modelViewPosition = modelViewMatrix * vec4(position, 1.0);
        gl_Position = projectionMatrix * modelViewPosition; 
      }
    `
}

let ps1_material =  new THREE.ShaderMaterial({
    //uniforms: uniforms,
    //fragmentShader: fragmentShader(),
    vertexShader: vertexShader(),
  })

// {
//   const loader2 = new THREE.CubeTextureLoader();
//   const texture = loader2.load([
//     'spacecubemap3/x+.png',
//     'spacecubemap3/x-.png',
//     'spacecubemap3/y+.png',
//     'spacecubemap3/y-.png',
//     'spacecubemap3/z+.png',
//     'spacecubemap3/z-.png',
//   ]);
//   scene.background = texture;
// }

camera.position.z = 2;
var model_1;
var mixer;
var helper;
var skeleton_1;
// loader.load( 'marie2020_2.glb', function ( gltf ) {
loader.load( 'mary.glb', function ( gltf ) {
    gltf.scene.traverse( function( node ) {
        if ( node.isMesh ) { 
            node.castShadow = true;
            //node.material = ps1_material;
        }
    } );
    model_1 = gltf.scene;
    mixer = new THREE.AnimationMixer( model_1 );
    gltf.animations.forEach( ( clip ) => { mixer.clipAction( clip ).play(); } ); //play animation

    // helper = new THREE.SkeletonHelper(model_1); //Show skeleton
    // helper.material.lineWidth = 1;
    // helper.visible = true;
    
    model_1.position.x = -.15;
    model_1.position.y = -1.2;
    model_1.position.z = -1;
    model_1.rotation.y = -5.2;
    model_1.rotation.x = .1;

    //apparently this gives the skinned mesh from scene but gltf.scene seems to be a skinnedmesh
    // gltf.scene.traverse( function ( child ) {
    //     if ( child.type == 'SkinnedMesh' ) {
    //         obj_1 = { animations: gltf.animations, mesh: child };
    //         skeleton_1 = obj_1.mesh.skeleton;
    //         //console.log(obj_1.mesh.skeleton.bones[10].position);
    //     }
    // });

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

    //WHY THIS SHIT DONT WORK
    // if(getMaxBone){
    //     if(model_1){
    //         getMaxBone = false;
    //         console.log(model_1.children[1].children[2].skeleton.bones);
    //         for(var i = 0; i < model_1.children[1].children[2].skeleton.bones.length; i++){
    //             var p = new THREE.Vector3();
    //             var q = new THREE.Quaternion();
    //             var s = new THREE.Vector3();
    //             model_1.children[1].children[2].skeleton.bones[i].matrixWorld.decompose(p, q, s); //sets by reference
    //             console.log(p);
    //             if( currMaxPosition.y < p.y){
    //                 maxBone = i;
    //                 currMaxPosition.x = p.x;
    //                 currMaxPosition.y = p.y;
    //                 currMaxPosition.z = p.z;
    //             }
    //         }
    //     }
    //     console.log(maxBone);
    // }
    //console.log(obj_1.mesh.skeleton.bones[10].position);
    //console.log("HELLO FUCKER");
    // if(model_1){

    //     //this will get the world position of the bone (bone[6].position is local or something, not sure)
    //     var p = new THREE.Vector3();
    //     var q = new THREE.Quaternion();
    //     var s = new THREE.Vector3();
    //     model_1.children[1].children[2].skeleton.bones[6].matrixWorld.decompose(p, q, s); //sets by reference

    //     //you have to set position like this for some reason, you can't do camera.position = p;
    //     //X AND Y MULTIPLIED BY 2 FOR NOW TO EXAGGERATE MOVEMENT
    //     camera.position.x = p.x * 2;
    //     camera.position.y = p.y * 2 - .4; // - .4 get the head more centrally
    //     camera.position.z = p.z + 3; //the 3 is the starting position
    // }
    //camera.position.x = skeleton_1.bones[0].position.x;
    requestAnimationFrame( animate );
    var delta = clock.getDelta();
    if ( mixer ) mixer.update( delta );
    //system.update();
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