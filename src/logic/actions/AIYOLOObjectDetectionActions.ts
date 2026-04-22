import {ImageData, LabelName, LabelRect} from '../../store/labels/types';
import {LabelsSelector} from '../../store/selectors/LabelsSelector';
import {ImageRepository} from '../imageRepository/ImageRepository';
import {AISelector} from '../../store/selectors/AISelector';
import {DetectedObject} from 'yolov5js';
import {findLast} from 'lodash';
import {v4 as uuidv4} from 'uuid';
import {LabelStatus} from '../../data/enums/LabelStatus';
import {store} from '../../index';
import {updateActiveLabelNameId, updateImageDataById} from '../../store/labels/actionCreators';
import {updateActivePopupType} from '../../store/general/actionCreators';
import {PopupWindowType} from '../../data/enums/PopupWindowType';
import {YOLOV5ObjectDetector} from '../../ai/YOLOV5ObjectDetector';

export class AIYOLOObjectDetectionActions {
    public static detectRectsForActiveImage(): void {
        const activeImageData: ImageData = LabelsSelector.getActiveImageData();
        AIYOLOObjectDetectionActions.detectRects(activeImageData.id, ImageRepository.getById(activeImageData.id))
    }

    public static detectRects(imageId: string, image: HTMLImageElement): void {
        if (LabelsSelector.getImageDataById(imageId).isVisitedByYOLOObjectDetector
            || !AISelector.isAIYOLOObjectDetectorModelLoaded())
            return;

        store.dispatch(updateActivePopupType(PopupWindowType.LOADER));
        YOLOV5ObjectDetector.predict(image, (predictions: DetectedObject[]) => {
            const detectionLabelId = AIYOLOObjectDetectionActions.resolveDetectionLabelId();
            store.dispatch(updateActivePopupType(null));
            if (!detectionLabelId) return;
            AIYOLOObjectDetectionActions.saveRectPredictions(imageId, predictions, detectionLabelId);
        })
    }

    public static saveRectPredictions(imageId: string, predictions: DetectedObject[], labelId: string) {
        const imageData: ImageData = LabelsSelector.getImageDataById(imageId);
        const predictedLabels: LabelRect[] = AIYOLOObjectDetectionActions.mapPredictionsToRectLabels(predictions, labelId);
        const nextImageData: ImageData = {
            ...imageData,
            labelRects: imageData.labelRects.concat(predictedLabels),
            isVisitedByYOLOObjectDetector: true
        };
        store.dispatch(updateImageDataById(imageData.id, nextImageData));
    }

    private static mapPredictionsToRectLabels(predictions: DetectedObject[], labelId: string): LabelRect[] {
        return predictions.map((prediction: DetectedObject) => {
            return {
                id: uuidv4(),
                labelIndex: null,
                labelId,
                rect: {
                    x: prediction.x,
                    y: prediction.y,
                    width: prediction.width,
                    height: prediction.height,
                },
                isVisible: true,
                isCreatedByAI: true,
                status: LabelStatus.ACCEPTED,
                suggestedLabel: '',
                confidence: (prediction as DetectedObject & {score?: number; confidence?: number}).score
                    ?? (prediction as DetectedObject & {score?: number; confidence?: number}).confidence
            }
        })
    }

    private static resolveDetectionLabelId(): string | null {
        const activeLabelNameId = LabelsSelector.getActiveLabelNameId();
        const labelNames = LabelsSelector.getLabelNames();
        if (!!activeLabelNameId && !!findLast(labelNames, {id: activeLabelNameId})) {
            return activeLabelNameId;
        }

        const firstLabel = labelNames[0];
        if (!firstLabel) {
            return null;
        }

        store.dispatch(updateActiveLabelNameId(firstLabel.id));
        return firstLabel.id;
    }

    public static extractNewSuggestedLabelNames(labels: LabelName[], predictions: DetectedObject[]): string[] {
        return predictions.reduce((acc: string[], prediction: DetectedObject) => {
            if (!acc.includes(prediction.class) && !findLast(labels, {name: prediction.class})) {
                acc.push(prediction.class)
            }
            return acc;
        }, [])
    }

    public static acceptAllSuggestedRectLabels(imageData: ImageData) {
        const newImageData: ImageData = {
            ...imageData,
            labelRects: imageData.labelRects.map((labelRect: LabelRect) => {
                const labelName: LabelName = findLast(LabelsSelector.getLabelNames(), {name: labelRect.suggestedLabel});
                return {
                    ...labelRect,
                    status: LabelStatus.ACCEPTED,
                    labelId: !!labelName ? labelName.id : labelRect.labelId
                }
            })
        };
        store.dispatch(updateImageDataById(newImageData.id, newImageData));
    }

    public static rejectAllSuggestedRectLabels(imageData: ImageData) {
        const newImageData: ImageData = {
            ...imageData,
            labelRects: imageData.labelRects.filter((labelRect: LabelRect) => labelRect.status === LabelStatus.ACCEPTED)
        };
        store.dispatch(updateImageDataById(newImageData.id, newImageData));
    }
}
