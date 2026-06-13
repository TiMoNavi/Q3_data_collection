export type ControllerAxisOverlaySchemaVersion = "controller-axis-overlay/v1";
export type ControllerAxisOverlayCoordinateSystem = "unity-world-meters";
export type ControllerAxisOverlayCameraModel = "quest-pca-pinhole";
export type ControllerHand = "left" | "right";
export type AxisName = "x" | "y" | "z";
export type ColorRgb = [number, number, number];
export type Matrix4x4 = [
  [number, number, number, number],
  [number, number, number, number],
  [number, number, number, number],
  [number, number, number, number]
];

export interface Vector2 {
  x: number;
  y: number;
}

export interface Vector3 {
  x: number;
  y: number;
  z: number;
}

export interface Quaternion {
  x: number;
  y: number;
  z: number;
  w: number;
}

export interface Size2i {
  width: number;
  height: number;
}

export interface ControllerAxisOverlayExport {
  schemaVersion: ControllerAxisOverlaySchemaVersion;
  coordinateSystem: ControllerAxisOverlayCoordinateSystem;
  cameraModel: ControllerAxisOverlayCameraModel;
  axisLengthMeters: number;
  source: ControllerAxisOverlaySource;
  frames: ControllerAxisOverlayFrame[];
}

export interface ControllerAxisOverlaySource {
  sessionDir: string;
  metadataPath: string;
  videoHeadersPath: string;
  generatedAtUnixMs?: number;
  toolName: string;
}

export interface ControllerAxisOverlayFrame {
  sourceCameraFrameId: number;
  encodedFrameId?: number;
  sequenceId?: number;
  timestampUnixMs: number;
  imagePath: string;
  overlayImagePath?: string;
  imageSize: Size2i;
  camera: OverlayCameraFrame;
  controllers: {
    left: OverlayController;
    right: OverlayController;
  };
}

export interface OverlayCameraFrame {
  frameId: number;
  timestampUnixMs: number;
  positionWorld: Vector3;
  rotationWorld: Quaternion;
  resolution: Size2i;
  sensorResolution: Size2i;
  focalLength: Vector2;
  principalPoint: Vector2;
  cameraLocalToWorldMatrix: Matrix4x4;
  cameraWorldToLocalMatrix: Matrix4x4;
}

export interface OverlayController {
  hand: ControllerHand;
  isValid: boolean;
  skipReason?: string;
  timestampUnixMs?: number;
  positionWorld?: Vector3;
  rotationWorld?: Quaternion;
  pivotPixel?: Vector2;
  axes?: Record<AxisName, AxisProjection>;
}

export interface AxisProjection {
  isVisible: boolean;
  skipReason?: string;
  endpointWorld?: Vector3;
  startPixel?: Vector2;
  endPixel?: Vector2;
  colorRgb: ColorRgb;
}
