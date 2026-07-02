The goal of this project is to create a library which allows the creation of block based vehicles/physics objects in Vintage Story. I'm attempting to build off of the existing minidimension systems that exist in the game code already.
This is currently utilising the work of Davis99 "PhysicsLib", which I have forked and modified to allow collisions to work correctly.
The next major thing I want to work on is getting proper rigidbody physics working, which is something that has been achieved by several modders in the past.


The most important thing to get working is getting collisions to work for vehicles, which is a 3 part problem:
-Getting vehicles to have to capability of moving/being affected by physics. (Gravity)
-Getting vehicles to correctly collide with the world terrain.

Major things that need to be worked on/fixed:
Reworking PhysicsLib DynamicPhysicsBehaviour to tick at the same rate as other motion in the game (preventing stuttering)

Lower priority issues to address:
Coming up with a method of assigning blocks to be turned into a rigidbody from the world. (Explore block reinforcing)
Coming up with a method for iterating through adjacent blocks and checking for the relevant assignment.
Preserve rigidbodies through saving and loading.
Set up config options (No. of rigidbodies per player, probably other stuff)
Rework PhysicsLib DynamicPhysicsBehaviour to use friction and other block properties to affect entities differently (Climbable, Slowing)
Find a way to make liquids make sense

Not yet possible:
-Coming up with survival integration
