import {LabelType} from '../../data/enums/LabelType';
import {LabelsSelector} from '../../store/selectors/LabelsSelector';
import {AISSDObjectDetectionActions} from './AISSDObjectDetectionActions';
import {AIPoseDetectionActions} from './AIPoseDetectionActions';
import {ImageData, LabelName, LabelPoint, LabelRect} from '../../store/labels/types';
import {AISelector} from '../../store/selectors/AISelector';
import {AIYOLOObjectDetectionActions} from './AIYOLOObjectDetectionActions';
import { AIRoboflowAPIObjectDetectionActions } from './AIRoboflowAPIObjectDetectionActions';
import {LabelStatus} from '../../data/enums/LabelStatus';
import {findLast} from 'lodash';
import {store} from '../../index';
import {updateImageDataById} from '../../store/labels/actionCreators';

export class AIActions {
    public static excludeRejectedLabelNames(suggestedLabels: string[], rejectedLabels: string[]): string[] {
        return suggestedLabels.reduce((acc: string[], label: string) => {
            if (!rejectedLabels.includes(label)) {
                acc.push(label)
            }
            return acc;
        }, [])
    }

    public static detect(imageId: string, image: HTMLImageElement): void {
        const imageData =  LabelsSelector.getImageDataById(imageId)
        const activeLabelType: LabelType = LabelsSelector.getActiveLabelType();
        const isAIYOLOObjectDetectorModelLoaded = AISelector.isAIYOLOObjectDetectorModelLoaded();
        const isAISSDObjectDetectorModelLoaded = AISelector.isAISSDObjectDetectorModelLoaded();
        const isRoboflowAPIModelLoaded = AISelector.isRoboflowAPIModelLoaded();
        switch (activeLabelType) {
            case LabelType.RECT:
                if (isAISSDObjectDetectorModelLoaded) {
                    AISSDObjectDetectionActions.detectRects(imageId, image);
                }
                if (isAIYOLOObjectDetectorModelLoaded) {
                    AIYOLOObjectDetectionActions.detectRects(imageId, image);
                }
                if (isRoboflowAPIModelLoaded) {
                    AIRoboflowAPIObjectDetectionActions.detectRects(imageData)
                }
                break;
            case LabelType.POINT:
                AIPoseDetectionActions.detectPoses(imageId, image);
                break;
        }
    }

    public static rejectAllSuggestedLabels(imageData: ImageData) {
        const activeLabelType: LabelType = LabelsSelector.getActiveLabelType();
        const isAIYOLOObjectDetectorModelLoaded = AISelector.isAIYOLOObjectDetectorModelLoaded();
        const isAISSDObjectDetectorModelLoaded = AISelector.isAISSDObjectDetectorModelLoaded();
        const isRoboflowAPIModelLoaded = AISelector.isRoboflowAPIModelLoaded();
        switch (activeLabelType) {
            case LabelType.RECT:
                if (isAISSDObjectDetectorModelLoaded) {
                    AISSDObjectDetectionActions.rejectAllSuggestedRectLabels(imageData);
                }
                if (isAIYOLOObjectDetectorModelLoaded) {
                    AIYOLOObjectDetectionActions.rejectAllSuggestedRectLabels(imageData);
                }
                if (isRoboflowAPIModelLoaded) {
                    AIRoboflowAPIObjectDetectionActions.rejectAllSuggestedRectLabels(imageData)
                }
                break;
            case LabelType.POINT:
                AIPoseDetectionActions.rejectAllSuggestedPointLabels(imageData);
                break;
        }
    }

    public static acceptAllSuggestedLabels(imageData: ImageData) {
        const activeLabelType: LabelType = LabelsSelector.getActiveLabelType();
        const isAIYOLOObjectDetectorModelLoaded = AISelector.isAIYOLOObjectDetectorModelLoaded();
        const isAISSDObjectDetectorModelLoaded = AISelector.isAISSDObjectDetectorModelLoaded();
        const isRoboflowAPIModelLoaded = AISelector.isRoboflowAPIModelLoaded();
        switch (activeLabelType) {
            case LabelType.RECT:
                if (isAISSDObjectDetectorModelLoaded) {
                    AISSDObjectDetectionActions.acceptAllSuggestedRectLabels(imageData);
                }
                if (isAIYOLOObjectDetectorModelLoaded) {
                    AIYOLOObjectDetectionActions.acceptAllSuggestedRectLabels(imageData);
                }
                if (isRoboflowAPIModelLoaded) {
                    AIRoboflowAPIObjectDetectionActions.acceptAllSuggestedRectLabels(imageData)
                }
                break;
            case LabelType.POINT:
                AIPoseDetectionActions.acceptAllSuggestedPointLabels(imageData);
                break;
        }
    }

    public static acceptAllSuggestedLabelsOnImages(imagesData: ImageData[]) {
        const activeLabelType: LabelType = LabelsSelector.getActiveLabelType();
        const labelNames = LabelsSelector.getLabelNames();

        switch (activeLabelType) {
            case LabelType.RECT:
                imagesData.forEach((imageData: ImageData) => {
                    const nextLabelRects = imageData.labelRects.map((labelRect: LabelRect) => {
                        if (!labelRect.isCreatedByAI || labelRect.status === LabelStatus.ACCEPTED) {
                            return labelRect;
                        }

                        const labelName: LabelName = findLast(labelNames, {name: labelRect.suggestedLabel});
                        return {
                            ...labelRect,
                            status: LabelStatus.ACCEPTED,
                            labelId: !!labelName ? labelName.id : labelRect.labelId
                        }
                    });

                    store.dispatch(updateImageDataById(imageData.id, {
                        ...imageData,
                        labelRects: nextLabelRects
                    }));
                });
                break;
            case LabelType.POINT:
                imagesData.forEach((imageData: ImageData) => {
                    const nextLabelPoints = imageData.labelPoints.map((labelPoint: LabelPoint) => {
                        if (!labelPoint.isCreatedByAI || labelPoint.status === LabelStatus.ACCEPTED) {
                            return labelPoint;
                        }

                        const labelName: LabelName = findLast(labelNames, {name: labelPoint.suggestedLabel});
                        return {
                            ...labelPoint,
                            status: LabelStatus.ACCEPTED,
                            labelId: !!labelName ? labelName.id : labelPoint.labelId
                        }
                    });

                    store.dispatch(updateImageDataById(imageData.id, {
                        ...imageData,
                        labelPoints: nextLabelPoints
                    }));
                });
                break;
        }
    }
}
