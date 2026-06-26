The goal of this project is to create a library which allows the creation of block based vehicles/physics objects in Vintage Story. I'm attempting to build off of the existing minidimension systems that exist in the game code already.
The main problem is that the existing system does not allow for players/other entities to collide with the object. To address this, I have created a type of cuboid called a PsuedoCuboid (what a creative name) that tracks a central position, length, width, height, and rotation in Quaternion form.
Another big barrier is that it seems like the existing testship function in the game does not render in the latest version of the game. I am at this point unsure if that is an issue for just me or if this is a broader issue. There are specific blocks that didn't render in 1.19.8, like doors and gears.
The third barrier is not being able to interact with blocks like chests or beds.

The most important thing to get working is getting collisions to work for vehicles, which is a 3 part problem:
-Getting entities to correctly collide with vehicles.
-Getting vehicles to have to capability of moving/being affected by physics.
-Getting vehicles to correctly collide with the world terrain.

Major things that need to be worked on/fixed:
-PsuedoCuboids do not yet render an outline
-Don't have an easy way to put PsuedoCuboids into the game world for testing purposes.

Lower priority issues to address:
-Coming up with a way of getting a group of blocks to be part of the same vehicle all at once.

Not yet possible:
-Coming up with survival integration
