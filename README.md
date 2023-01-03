# Pathtracer-RayStorm

## 介绍

基于Unity-ComputeShader的RayStorm路径追踪器。

## 效果

<p align="center">
	<img src="Document/Diffuse.jpg" width="600px"/>
	<center style="color:#C0C0C0"> Diffuse  </center>
</p>

<p align="center">
	<img src="Document/Mirror.jpg" width="600px"/>
	<center style="color:#C0C0C0"> Mirror  </center>
</p>

<p align="center">
	<img src="Document/Transparency.jpg" width="600px"/>
	<center style="color:#C0C0C0"> Mirror  </center>
</p>

<p align="center">
	<img src="Document/ExampleScene.jpg" width="600px"/>
	<center style="color:#C0C0C0"> Example  </center>
</p>

## 特性

* Unity场景构建
  * 将Unity中搭建好的场景导入到RayStorm中进行渲染
* 材质
  * 漫反射（石膏）
  * 金属材质
  * 非透明电介质（塑料、陶瓷）
  * 透明电介质（玻璃）
* 优化
  * 场景八叉树