import React from 'react';
import {connect} from "react-redux";
import {LabelType} from "../../../../data/enums/LabelType";
import {ISize} from "../../../../interfaces/ISize";
import {AppState} from "../../../../store";
import {ImageData, LabelPoint, LabelRect} from "../../../../store/labels/types";
import {VirtualList} from "../../../Common/VirtualList/VirtualList";
import ImagePreview from "../ImagePreview/ImagePreview";
import './ImagesList.scss';
import {ContextManager} from "../../../../logic/context/ContextManager";
import {ContextType} from "../../../../data/enums/ContextType";
import {ImageActions} from "../../../../logic/actions/ImageActions";
import {EventType} from "../../../../data/enums/EventType";
import {LabelStatus} from "../../../../data/enums/LabelStatus";
import {UnderlineTextButton} from "../../../Common/UnderlineTextButton/UnderlineTextButton";
import {AIActions} from "../../../../logic/actions/AIActions";

interface IProps {
    activeImageIndex: number;
    imagesData: ImageData[];
    activeLabelType: LabelType;
}

interface IState {
    size: ISize;
    showDetectionsOnly: boolean;
}

class ImagesList extends React.Component<IProps, IState> {
    private imagesListBodyRef: HTMLDivElement;

    constructor(props) {
        super(props);

        this.state = {
            size: null,
            showDetectionsOnly: false,
        }
    }

    public componentDidMount(): void {
        this.updateListSize();
        window.addEventListener(EventType.RESIZE, this.updateListSize);
    }

    public componentWillUnmount(): void {
        window.removeEventListener(EventType.RESIZE, this.updateListSize);
    }

    private updateListSize = () => {
        if (!this.imagesListBodyRef)
            return;

        const listBoundingBox = this.imagesListBodyRef.getBoundingClientRect();
        this.setState({
            size: {
                width: listBoundingBox.width,
                height: listBoundingBox.height
            }
        })
    };

    private isImageChecked = (index:number): boolean => {
        const imageData = this.props.imagesData[index]
        switch (this.props.activeLabelType) {
            case LabelType.LINE:
                return imageData.labelLines.length > 0
            case LabelType.IMAGE_RECOGNITION:
                return imageData.labelNameIds.length > 0
            case LabelType.POINT:
                return imageData.labelPoints
                    .filter((labelPoint: LabelPoint) => labelPoint.status === LabelStatus.ACCEPTED)
                    .length > 0
            case LabelType.POLYGON:
                return imageData.labelPolygons.length > 0
            case LabelType.RECT:
                return imageData.labelRects
                    .filter((labelRect: LabelRect) => labelRect.status === LabelStatus.ACCEPTED)
                    .length > 0
        }
    };

    private hasDetections = (imageData: ImageData): boolean => {
        switch (this.props.activeLabelType) {
            case LabelType.LINE:
                return imageData.labelLines.length > 0;
            case LabelType.IMAGE_RECOGNITION:
                return imageData.labelNameIds.length > 0;
            case LabelType.POINT:
                return imageData.labelPoints.length > 0;
            case LabelType.POLYGON:
                return imageData.labelPolygons.length > 0;
            case LabelType.RECT:
                return imageData.labelRects.length > 0;
        }
    };

    private onClickHandler = (index: number) => {
        ImageActions.getImageByIndex(index)
    };

    private getVisibleImageIndexes = (): number[] => {
        const allIndexes = this.props.imagesData.map((imageData: ImageData, index: number) => index);

        if (!this.state.showDetectionsOnly) {
            return allIndexes;
        }

        return allIndexes.filter((index: number) => this.hasDetections(this.props.imagesData[index]));
    };

    private getDetectedImageIndexes = (): number[] => {
        return this.props.imagesData
            .map((imageData: ImageData, index: number) => index)
            .filter((index: number) => this.hasDetections(this.props.imagesData[index]));
    };

    private hasPendingDetections = (imageData: ImageData): boolean => {
        switch (this.props.activeLabelType) {
            case LabelType.POINT:
                return imageData.labelPoints.some((labelPoint: LabelPoint) =>
                    labelPoint.isCreatedByAI && labelPoint.status !== LabelStatus.ACCEPTED);
            case LabelType.RECT:
                return imageData.labelRects.some((labelRect: LabelRect) =>
                    labelRect.isCreatedByAI && labelRect.status !== LabelStatus.ACCEPTED);
            default:
                return false;
        }
    };

    private getImagesWithPendingDetectionsCount = (): number => {
        return this.props.imagesData.filter((imageData: ImageData) => this.hasPendingDetections(imageData)).length;
    };

    private toggleDetectionsOnly = () => {
        this.setState((state: IState) => ({
            showDetectionsOnly: !state.showDetectionsOnly
        }), this.updateListSize);
    };

    private validateAllImages = () => {
        AIActions.acceptAllSuggestedLabelsOnImages(this.props.imagesData);
    };

    private renderImagePreview = (visibleIndex: number, isScrolling: boolean, isVisible: boolean, style: React.CSSProperties) => {
        const imageIndex = this.getVisibleImageIndexes()[visibleIndex];
        const previewSize = this.getPreviewSize();
        return <ImagePreview
            key={imageIndex}
            style={style}
            size={previewSize}
            isScrolling={isScrolling}
            isChecked={this.isImageChecked(imageIndex)}
            imageData={this.props.imagesData[imageIndex]}
            onClick={() => this.onClickHandler(imageIndex)}
            isSelected={this.props.activeImageIndex === imageIndex}
        />
    };

    private getPreviewSize = (): ISize => {
        const {size} = this.state;

        if (!size) {
            return {width: 150, height: 150};
        }

        // Keep two readable columns in the images sidebar while avoiding a fallback to a single column.
        const previewWidth = Math.max(140, Math.min(180, Math.floor((size.width - 12) / 2)));
        return {
            width: previewWidth,
            height: previewWidth
        };
    };

    public render() {
        const { size } = this.state;
        const detectedImageIndexes = this.getDetectedImageIndexes();
        const imagesWithPendingDetectionsCount = this.getImagesWithPendingDetectionsCount();
        const visibleImageIndexes = this.getVisibleImageIndexes();
        const previewSize = this.getPreviewSize();
        return(
            <div
                className="ImagesList"
                onClick={() => ContextManager.switchCtx(ContextType.LEFT_NAVBAR)}
            >
                <div className="ImagesListHeader">
                    <UnderlineTextButton
                        label={`Detections only (${detectedImageIndexes.length}/${this.props.imagesData.length})`}
                        active={this.state.showDetectionsOnly}
                        under={true}
                        onClick={this.toggleDetectionsOnly}
                    />
                    <UnderlineTextButton
                        label={`Validate all (${imagesWithPendingDetectionsCount})`}
                        under={true}
                        onClick={this.validateAllImages}
                    />
                </div>
                <div
                    className="ImagesListBody"
                    ref={ref => this.imagesListBodyRef = ref}
                >
                    {!!size && visibleImageIndexes.length > 0 && <VirtualList
                        size={size}
                        childSize={previewSize}
                        childCount={visibleImageIndexes.length}
                        childRender={this.renderImagePreview}
                        overScanHeight={200}
                    />}
                    {!!size && visibleImageIndexes.length === 0 &&
                        <div className="ImagesListEmpty">
                            No images with detections
                        </div>}
                </div>
            </div>
        )
    }
}

const mapDispatchToProps = {};

const mapStateToProps = (state: AppState) => ({
    activeImageIndex: state.labels.activeImageIndex,
    imagesData: state.labels.imagesData,
    activeLabelType: state.labels.activeLabelType
});

export default connect(
    mapStateToProps,
    mapDispatchToProps
)(ImagesList);
