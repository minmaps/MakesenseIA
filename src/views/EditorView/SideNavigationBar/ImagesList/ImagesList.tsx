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
import {updateShowAIConfidenceStatus} from "../../../../store/general/actionCreators";

enum ImageFilterMode {
    ALL = 'all',
    WITH_ANNOTATIONS = 'withAnnotations',
    WITH_SUGGESTIONS = 'withSuggestions',
    WITHOUT_SUGGESTIONS = 'withoutSuggestions',
    WITHOUT_ANNOTATIONS = 'withoutAnnotations'
}

interface IProps {
    activeImageIndex: number;
    imagesData: ImageData[];
    activeLabelType: LabelType;
    showAIConfidence: boolean;
    updateShowAIConfidenceStatus: (showAIConfidence: boolean) => any;
}

interface IState {
    size: ISize;
    filterMode: ImageFilterMode;
    minConfidence: number;
    maxConfidence: number;
}

class ImagesList extends React.Component<IProps, IState> {
    private imagesListBodyRef: HTMLDivElement;

    constructor(props) {
        super(props);

        this.state = {
            size: null,
            filterMode: ImageFilterMode.ALL,
            minConfidence: 0,
            maxConfidence: 100,
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
        return this.hasActiveToolAnnotations(imageData);
    };

    private hasActiveToolAnnotations = (imageData: ImageData): boolean => {
        switch (this.props.activeLabelType) {
            case LabelType.LINE:
                return imageData.labelLines.length > 0;
            case LabelType.IMAGE_RECOGNITION:
                return imageData.labelNameIds.length > 0;
            case LabelType.POINT:
                return imageData.labelPoints.some((labelPoint: LabelPoint) =>
                    labelPoint.status === LabelStatus.ACCEPTED);
            case LabelType.POLYGON:
                return imageData.labelPolygons.length > 0;
            case LabelType.RECT:
                return imageData.labelRects.some((labelRect: LabelRect) =>
                    labelRect.status === LabelStatus.ACCEPTED);
        }
    };

    private hasAnnotations = (imageData: ImageData): boolean => {
        return imageData.labelLines.length > 0 ||
            imageData.labelNameIds.length > 0 ||
            imageData.labelPolygons.length > 0 ||
            imageData.labelRects.some((labelRect: LabelRect) => labelRect.status === LabelStatus.ACCEPTED) ||
            imageData.labelPoints.some((labelPoint: LabelPoint) => labelPoint.status === LabelStatus.ACCEPTED);
    };

    private hasSuggestions = (imageData: ImageData): boolean => {
        return imageData.labelRects.some((labelRect: LabelRect) => this.isSuggestedLabel(labelRect)) ||
            imageData.labelPoints.some((labelPoint: LabelPoint) => this.isSuggestedLabel(labelPoint));
    };

    private isSuggestedLabel = (label: LabelRect | LabelPoint): boolean => {
        return label.isCreatedByAI && label.status !== LabelStatus.ACCEPTED;
    };

    private onClickHandler = (index: number) => {
        ImageActions.getImageByIndex(index)
    };

    private getVisibleImageIndexes = (): number[] => {
        const allIndexes = this.props.imagesData.map((imageData: ImageData, index: number) => index);

        return allIndexes.filter((index: number) => this.matchesFilters(this.props.imagesData[index]));
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

    private matchesFilters = (imageData: ImageData): boolean => {
        const matchesMode = (() => {
            switch (this.state.filterMode) {
                case ImageFilterMode.WITH_ANNOTATIONS:
                    return this.hasActiveToolAnnotations(imageData);
                case ImageFilterMode.WITH_SUGGESTIONS:
                    return this.hasSuggestions(imageData);
                case ImageFilterMode.WITHOUT_SUGGESTIONS:
                    return !this.hasSuggestions(imageData);
                case ImageFilterMode.WITHOUT_ANNOTATIONS:
                    return !this.hasAnnotations(imageData);
                default:
                    return true;
            }
        })();

        return matchesMode && this.matchesConfidenceRange(imageData);
    };

    private matchesConfidenceRange = (imageData: ImageData): boolean => {
        if (!this.isConfidenceRangeActive() ||
            this.state.filterMode === ImageFilterMode.WITHOUT_SUGGESTIONS ||
            this.state.filterMode === ImageFilterMode.WITHOUT_ANNOTATIONS) {
            return true;
        }

        const confidenceValues = imageData.labelRects
            .filter((labelRect: LabelRect) => labelRect.isCreatedByAI && labelRect.confidence !== undefined)
            .map((labelRect: LabelRect) => this.toConfidencePercent(labelRect.confidence))
            .concat(imageData.labelPoints
                .filter((labelPoint: LabelPoint) => labelPoint.isCreatedByAI && labelPoint.confidence !== undefined)
                .map((labelPoint: LabelPoint) => this.toConfidencePercent(labelPoint.confidence)));

        return confidenceValues.some((confidence: number) =>
            confidence >= this.state.minConfidence && confidence <= this.state.maxConfidence);
    };

    private isConfidenceRangeActive = (): boolean => {
        return this.state.minConfidence > 0 || this.state.maxConfidence < 100;
    };

    private toConfidencePercent = (confidence: number): number => {
        const percent = confidence <= 1 ? confidence * 100 : confidence;
        return Math.min(100, Math.max(0, percent));
    };

    private onFilterModeChange = (event: React.ChangeEvent<HTMLSelectElement>) => {
        this.setState({
            filterMode: event.target.value as ImageFilterMode
        }, this.updateListSize);
    };

    private onMinimumConfidenceChange = (event: React.ChangeEvent<HTMLInputElement>) => {
        const minConfidence = Math.min(Number(event.target.value), this.state.maxConfidence);
        this.setState({minConfidence}, this.updateListSize);
    };

    private onMaximumConfidenceChange = (event: React.ChangeEvent<HTMLInputElement>) => {
        const maxConfidence = Math.max(Number(event.target.value), this.state.minConfidence);
        this.setState({maxConfidence}, this.updateListSize);
    };

    private onShowConfidenceChange = (event: React.ChangeEvent<HTMLInputElement>) => {
        this.props.updateShowAIConfidenceStatus(event.target.checked);
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
            showConfidence={this.props.showAIConfidence}
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
        const confidenceRangeLabel = `${this.state.minConfidence}% - ${this.state.maxConfidence}%`;
        return(
            <div
                className="ImagesList"
                onClick={() => ContextManager.switchCtx(ContextType.LEFT_NAVBAR)}
            >
                <div className="ImagesListHeader">
                    <select
                        className="ImagesListFilterSelect"
                        value={this.state.filterMode}
                        onChange={this.onFilterModeChange}
                    >
                        <option value={ImageFilterMode.ALL}>All images ({visibleImageIndexes.length}/{this.props.imagesData.length})</option>
                        <option value={ImageFilterMode.WITH_ANNOTATIONS}>With annotations ({detectedImageIndexes.length})</option>
                        <option value={ImageFilterMode.WITH_SUGGESTIONS}>With suggestions</option>
                        <option value={ImageFilterMode.WITHOUT_SUGGESTIONS}>No suggestions</option>
                        <option value={ImageFilterMode.WITHOUT_ANNOTATIONS}>No annotations</option>
                    </select>
                    <UnderlineTextButton
                        label={`Validate all (${imagesWithPendingDetectionsCount})`}
                        under={true}
                        onClick={this.validateAllImages}
                    />
                    <div className="ImagesListConfidenceFilter">
                        <label className="ImagesListShowConfidence">
                            <input
                                type="checkbox"
                                checked={this.props.showAIConfidence}
                                onChange={this.onShowConfidenceChange}
                            />
                            <span>Show confidence</span>
                        </label>
                        <div className="ImagesListConfidenceHeader">
                            <span>Confidence</span>
                            <span>{confidenceRangeLabel}</span>
                        </div>
                        <label>
                            <span>Min</span>
                            <input
                                type="range"
                                min="0"
                                max="100"
                                step="1"
                                value={this.state.minConfidence}
                                onChange={this.onMinimumConfidenceChange}
                            />
                        </label>
                        <label>
                            <span>Max</span>
                            <input
                                type="range"
                                min="0"
                                max="100"
                                step="1"
                                value={this.state.maxConfidence}
                                onChange={this.onMaximumConfidenceChange}
                            />
                        </label>
                    </div>
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
                            No images match the filters
                        </div>}
                </div>
            </div>
        )
    }
}

const mapDispatchToProps = {
    updateShowAIConfidenceStatus
};

const mapStateToProps = (state: AppState) => ({
    activeImageIndex: state.labels.activeImageIndex,
    imagesData: state.labels.imagesData,
    activeLabelType: state.labels.activeLabelType,
    showAIConfidence: state.general.showAIConfidence
});

export default connect(
    mapStateToProps,
    mapDispatchToProps
)(ImagesList);
