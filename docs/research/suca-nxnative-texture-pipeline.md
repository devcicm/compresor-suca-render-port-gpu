# Investigación: fórmulas SUCA y tubería gráfica nxnative

## Evidencia inspeccionada

- SUCA: `docs/math/suca-formulas.md`, `src/Suca.Kernel/SucaCompressionKernel.cs` del repositorio devcicm/compresor-suca-render-y-pesos-de-imagen.
- nxnative: `seedds/nxnative`, `dm/gfx/nvn/nvn_tex.c` y `dm/gfx/nvn/nvn_dirty.c` (consultados 2026-11-01). Versionar commits exactos antes de medir.
- Esto es análisis estático; no hay pruebas de rendimiento ni ejecución de juegos.

## Qué calcula SUCA y qué no

1. `ImageAnalyzer` obtiene luminancia BT.601, contraste, entropía de histograma, Sobel/bordes, proyecciones, foreground y crestas.
2. `SucaMath.FactorEscalaBase` calcula escala heurística con perfil A/B/C/D, oscilación y penalización por bytes. `CalidadBase` deriva calidad. **El hash del perfil no es un codec ni reconstruye píxeles**.
3. `SucaDecisionEngine` elige escala, calidad y WebP/PNG/JPEG.
4. `SucaCompressor` usa ImageSharp para remuestreo Mitchell-Netravali y codificación. `PesoMosca` ajusta calidad/escala con búsqueda y aproximación por raíz cuadrada.
5. `IntegrityValidator` informa métricas perceptuales. Comparar imágenes igualando tamaño puede ocultar detalle perdido por downsampling.
6. El mapa 8×8 de pesos de preservación es **informativo**; la documentación dice que no alimenta `Decide` y no constituye una red neuronal.

SUCA opera sobre imágenes ya existentes. No sustituye transformaciones geométricas, rasterización, shading, profundidad ni presentación de fotogramas.

## Qué hace nxnative en la ruta gráfica

- El proyecto ejecuta ARM64 sobre Apple Silicon, provee servicios Horizon y adapta NVN a Metal. Un host Windows x86-64 necesitaría resolver además ejecución ARM64 y reemplazar las dependencias Apple.
- La traducción de shaders Maxwell -> SPIR-V se basa en Ryujinx; en Apple la ruta continúa hacia Metal.
- `nvn_tex.c` calcula tamaño, alineación y offsets de texturas NVN en layout block-linear de Tegra X1; reconoce bloques ASTC y BC/DXT/BPTC, mipmaps, arrays y cubemaps. **No sustituir esta lógica por heurísticas SUCA**, pues describe el layout requerido por el juego.
- `nvn_dirty.c` YA implementa detección de páginas modificadas y evita reuploads de texturas cuando no cambiaron; utiliza protección de páginas, generaciones atómicas y manejo cuidadoso de concurrencia. **No reinventar dirty tracking** sin medir un cuello de botella específico.
- La conversión de formato/layout y el upload son candidatos para observabilidad y experimentos de caché; las decisiones deben respetar formato, dimensiones, colorimetría, alfa, mipmaps, semántica y coherencia CPU/GPU.

## Hipótesis de integración, ordenadas por riesgo

| Hipótesis | Posible beneficio | Riesgo / falsación |
| --- | --- | --- |
| SUCA como analizador offline de texturas estáticas | Selección de formatos y política de caché | Costo del análisis supera ahorro; decisiones equivalentes a heurística simple |
| Peso SUCA por bloques/tiles como pista de prioridad | Streaming o selección de nivel de detalle bajo presupuesto | Bordes/entropía no predicen importancia visual real ni dependencia semántica |
| Cachear transcodificación por hash de contenido + descriptor + backend | Menos CPU y carga en reutilizaciones | Invalidez por páginas dirty, aliasing, cambios de formato o shaders |
| Preprocesar formatos de distribución fuera de la ruta crítica | Menos bytes de almacenamiento | Mayor latencia de decodificación, RAM/VRAM o fallos de calidad |
| Aplicar peso SUCA a texturas dinámicas | Potencial reducción de tráfico | Alta probabilidad de sobrecarga; no habilitar sin prueba aislada |

**Peligro central:** una textura WebP de 80 KB puede expandirse a varios MB en RGBA8 en GPU; no equivale a 80 KB de VRAM. Ejemplo teórico: 1024×1024 RGBA8 = 4 MiB por mip nivel 0; BC7 4×4 = 1 MiB (sin mipmaps), si es compatible y adecuado.

## Función objetivo

Minimizar tiempo de fotograma crítico (CPU, GPU y sincronización), tirones p95/p99, bytes transferidos y memoria residente, sujeto a equivalencia visual/semántica. No sumar CPU+GPU ingenuamente cuando se solapan.

`ganancia_neta = costo_baseline - (costo_optimizacion + costo_conversion + costo_invalidacion + costo_cache)`

La comparación debe medirse por escenarios, en unidades homogéneas, distinguiendo costos amortizados de costos por fotograma. Un ahorro de almacenamiento no implica ahorro de frame time.

## Experimento inicial: sin juego y sin modificar SUCA

1. Crear texturas sintéticas propias: color, alfa, normales, patrones UI, ruido y tiles con cambios controlados.
2. Validar layouts y tamaños calculados por `nvn_tex.c` con fixtures conocidos y baselines.
3. Comparar pipeline directo vs pipeline con análisis SUCA offline; dimensiones y mipmaps iguales.
4. Instrumentar decode, conversión layout, upload, hit/miss de caché, invalidación, RAM/VRAM y frame time p50/p95/p99.
5. Repetir con texturas estáticas y dinámicas; comprobar que dirty tracking preserva cambios, aliasing y sincronización.
6. Aceptar solo optimizaciones con ganancia estadísticamente reproducible, fidelidad y coherencia preservadas; fallback automático.

## Decisiones prohibidas

- No introducir WebP en el render loop sin prueba de latencia y memoria.
- No tratar hash A/B/C/D o mapa 8×8 como compresor de bloques GPU.
- No redimensionar texturas del juego sin contrato de calidad y semántica.
- No codificar ramas por modelo de GPU; descubrir capacidades.
- No reimplementar tracking de cambios ya presente en nxnative.
- No modificar formatos, offsets o layouts NVN por heurísticas de peso.
- No proclamar rendimiento o compatibilidad sin ejecución y métricas.

## Próxima inspección

Seguir flujo concreto desde descriptor NVN -> layout -> formato soportado/transcodificación -> upload -> invalidación -> uso en shader. Identificar funciones y puntos de instrumentación exactos en `dm/gfx/nvn`, antes de escribir un adaptador.
