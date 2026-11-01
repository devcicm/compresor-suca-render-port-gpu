# SUCA GPU Port — Laboratorio de compatibilidad y rendimiento

## Propósito

Investigar, mediante hipótesis falsables y experimentos reproducibles, si componentes del proyecto [SUCA](https://github.com/devcicm/compresor-suca-render-y-pesos-de-imagen) pueden aportar ventajas en el procesamiento, almacenamiento y transcodificación de texturas para distintas plataformas gráficas. Este repositorio **no** es un port funcional de nxnative ni un emulador.

## Principios de diseño

- **Sin hardware hardcodeado**: descubrir CPU, GPU, controladores, memoria, APIs, extensiones y formatos disponibles en tiempo de ejecución. Los equipos de prueba son muestras, no requisitos.
- **Sin cifras mágicas**: presupuestos, umbrales, objetivos de calidad y límites de tiempo son parámetros versionados por escenario, con unidades y procedencia.
- **Sin acoplamiento al codec**: separar análisis visual, política de selección, codec, almacenamiento, carga y medición mediante contratos intercambiables.
- **Sin suposiciones de portabilidad**: seleccionar capacidades negociadas y proporcionar fallback explícito; una prueba exitosa en un equipo no demuestra compatibilidad universal.
- **Sin cambios al SUCA original** durante la investigación: usar adaptadores o snapshots versionados, respetando licencia y atribución.
- **Sin afirmar resultados antes de medir**: distinguir hipótesis, evidencia, limitaciones y conclusiones.

## Hipótesis a contrastar

- H1: las señales visuales de SUCA pueden mejorar la selección de una representación de textura bajo restricciones explícitas.
- H2: una caché de transcodificación puede reducir tiempo de carga recurrente, considerando su costo de creación, invalidación y almacenamiento.
- H3: la compresión para distribución puede reducir bytes en disco o transferencia sin reducir necesariamente la memoria residente en GPU.
- H4: una política adaptativa basada en capacidades puede ofrecer comportamiento degradado controladamente entre GPUs distintas.

Cada hipótesis admite resultados negativos y requiere baseline independiente.

## Modelo conceptual

```text
Activo de prueba autorizado
  -> inspección y metadatos
  -> análisis visual (adaptador SUCA opcional)
  -> descubrimiento de capacidades de plataforma
  -> política configurable de representación
  -> codec/transcodificador intercambiable
  -> validación de integridad y dimensiones
  -> caché versionada (opcional)
  -> carga a GPU (si existe backend)
  -> métricas + evidencia reproducible
```

## Contratos conceptuales

- `ITextureSource`: datos, formato, dimensiones, mipmaps, colorimetría, alfa, procedencia.
- `ICapabilityProbe`: capacidades observadas de dispositivo, API y formatos.
- `IImageAnalyzer`: señales visuales y costo del análisis.
- `ITexturePolicy`: decisión explicable, restricciones y alternativas.
- `ITextureTranscoder`: transformación y costos medidos.
- `ITextureCache`: clave derivada de contenido, parámetros, versión de codec y capacidades; invalidación determinista.
- `IBenchmarkRunner`: calentamiento, repeticiones, aislamiento de variables y captura de evidencia.

Estos nombres son propuestas de contrato, no interfaces implementadas.

## Diseño experimental

1. Registrar el entorno automáticamente: CPU, GPU, VRAM disponible/reportada, driver, sistema operativo, API y formatos compatibles.
2. Definir corpus autorizado y versionado: fotografía, interfaz, gráficos, normales, mapas de datos, alfa y distintas resoluciones. No aplicar pérdidas a datos donde cambien su semántica.
3. Comparar contra controles: activo original, codificación convencional y formatos de bloques nativos soportados. Mantener dimensiones y mipmaps constantes cuando se compare fidelidad a igual resolución.
4. Separar **bytes de archivo**, **bytes de transferencia**, **memoria CPU**, **memoria GPU**, **latencia de codificación**, **latencia de decodificación**, **latencia de carga**, **calidad** y **coste de caché**.
5. Evaluar calidad con métricas adecuadas al contenido; el SSIM tras redimensionar no demuestra preservación de detalle a resolución original.
6. Repetir pruebas con calentamiento, distribución de tiempos, variabilidad, versiones de herramientas y criterios de aceptación configurables.
7. Publicar resultados positivos o negativos, con datos y limitaciones, sin extrapolar automáticamente a otros equipos.

## Relación con nxnative

La optimización de texturas es una línea de investigación **separada** de portar ejecución ARM64 y servicios de sistema, o de sustituir una API gráfica por otra. SUCA por sí solo no resuelve esas capas.

## Estado

Fase 0 — protocolo teórico. No hay benchmarks ejecutados, implementación GPU ni ganancias verificadas.

## Próximos hitos

- Documentar contratos y matriz de capacidades, sin modelos específicos hardcodeados.
- Definir fixtures y criterios de medición reproducibles.
- Crear un benchmark mínimo con baseline antes de incorporar análisis SUCA.
- Evaluar por separado el valor marginal de cada componente SUCA.

Repositorio de investigación; los resultados futuros deberán indicar commit, entorno, dataset, configuración y procedimiento de reproducción.
