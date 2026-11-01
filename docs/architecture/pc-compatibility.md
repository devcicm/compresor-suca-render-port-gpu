# Arquitectura conceptual de compatibilidad PC y SUCA

## Alcance y límites

Objetivo de investigación: evaluar la viabilidad de una capa de compatibilidad para ejecutar software ARM64 de Nintendo Switch original en hosts x86-64, y determinar qué aporta SUCA a la gestión de recursos gráficos. No se afirma compatibilidad funcional ni se distribuyen juegos, firmware, claves o recursos protegidos. Las pruebas iniciales usan programas y texturas sintéticas propios.

## Separación de responsabilidades

```text
Binario ARM64 autorizado / programa sintético
  -> Loader y memoria invitada
  -> Traductor de instrucciones ARM64 -> host x86-64
  -> Interfaz de servicios del sistema invitado (Horizon HLE)
  -> Intercepción de comandos gráficos (NVN)
  -> Modelo intermedio gráfico independiente del backend
  -> Backend seleccionado por capacidades (Vulkan, otros futuros)
  -> GPU del host

Texturas autorizadas
  -> Inventario de formato, dimensiones, mipmaps y semántica
  -> Análisis opcional SUCA
  -> Política de conversión dependiente de capacidades
  -> Transcodificador y caché verificable
  -> Recursos GPU
```

No asumir que nxnative expone componentes reutilizables: validar licencias, dependencias, interfaces y portabilidad del código fuente antes de proponer un fork.

## Contratos teóricos (no implementados)

- `IGuestExecutableLoader`: segmentos, relocaciones, permisos de memoria y arquitectura.
- `IInstructionExecutor`: ejecución/traducción y manejo de excepciones, sin acoplar al host.
- `ISystemServiceBridge`: llamadas de servicios, sincronización y semántica observable.
- `IGraphicsCommandDecoder`: comandos gráficos y recursos con validación de entradas.
- `IGraphicsIntermediateRepresentation`: comandos y sincronización independientes de Vulkan/Metal.
- `IGraphicsBackend`: consulta de capacidades, asignación de recursos, ejecución y presentación.
- `ITextureResourceAdapter`: formato, mipmaps, colorimetría, canales, datos y transcodificación.
- `IObservabilitySink`: latencias por etapa, fallos, trazas y consumo de recursos.

## Invariantes y restricciones

- Ningún identificador de fabricante/modelo debe seleccionar por sí solo una ruta de ejecución.
- La selección se basa en capacidades observadas y requisitos declarados.
- Texturas de datos (normales, profundidad, índices) requieren validación semántica; no aplicar compresión con pérdida genérica.
- Preservar orden de operaciones, barreras, sincronización y semántica de memoria.
- Los fallos de traducción deben ser explícitos; no sustituir silenciosamente instrucciones o servicios desconocidos.
- Separar compresión en disco de formatos residentes GPU: bytes de WebP no equivalen a VRAM utilizada.
- Sin hardcodear umbrales, tiempos, presupuestos ni formatos de dispositivo: configuración por escenario y evidencia.
- No introducir dependencias directas desde el traductor ARM64 hacia SUCA.

## Orden de experimentación

1. Verificar repositorio y licencias de nxnative, identificando qué piezas son específicas de Apple.
2. Construir inventario de dependencias y mapa de llamadas: CPU, Horizon, NVN, Metal.
3. Prototipo gráfico con comandos sintéticos propios y backend Vulkan; sin ejecutar juegos.
4. Añadir pruebas de equivalencia de recursos, sincronización y shaders.
5. Benchmark SUCA contra baselines nativos con mismas dimensiones y mipmaps.
6. Solo tras evidencia, investigar integración gradual con ejecutables ARM64 autorizados.

## Criterios de éxito inicial

Un triángulo sintético renderizado mediante una representación gráfica intermedia y un backend seleccionado por capacidades, con trazas reproducibles. La ejecución de un juego completo es una meta de largo plazo y no un criterio del primer hito.
