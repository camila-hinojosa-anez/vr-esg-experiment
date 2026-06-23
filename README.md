# 🧠 Eye-Tracking Decision-Making en Realidad Virtual (Unity)

Este repositorio contiene la implementación en **Unity 6** del experimento de toma de decisiones basado en seguimiento ocular, desarrollado para visores **Meta Quest Pro** usando el **Meta XR All-in-One SDK v74**.

## 🎯 Objetivo

Diseñar un entorno de realidad virtual donde los usuarios interactúan con gráficos financieros (u otras visualizaciones), permitiendo estudiar patrones de atención visual y tiempos de decisión.

## 🚀 Características

- Seguimiento ocular (eye-tracking)
- Seguimiento facial y de manos
- Botones interactivos con gestos o controladores
- Sistema modular para mostrar secuencialmente actividades
- Exportación de datos de eye-tracking en formato JSON

## 🧩 Componentes Principales

- `ExperimentManager.cs`: controla el flujo del experimento (calibración, actividades, decisiones).
- `EyeTrackingLogger.cs`: registra los datos de fijaciones, trayectorias y parpadeo.
- `ActivityResults` / `ExperimentResults`: estructuras serializadas que almacenan datos del experimento.
- Canvas UI en 3D interactivo, ajustado para verse correctamente en VR.

## 📤 Exportación de Datos

Los datos se exportan automáticamente al finalizar el experimento como archivos `.json` para su posterior análisis.

> 🔍 El análisis de estos datos se realiza en el repositorio complementario en Python:  
👉 [Análisis en Python – GazeVisualizer](https://github.com/VictoriaGuerriero/EyeTracking-API)

## 🛠 Requisitos

- Unity 6.0.0 o superior
- Meta XR All-in-One SDK v74
- Meta Quest Pro + Link / Wireless para pruebas
- Activado el soporte para OpenXR

