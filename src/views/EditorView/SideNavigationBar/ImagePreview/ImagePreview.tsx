import classNames from "classnames";
import React from 'react';
import { connect } from "react-redux";
import { ClipLoader } from "react-spinners";
import { ImageLoadManager } from "../../../../logic/imageRepository/ImageLoadManager";
import { IRect } from "../../../../interfaces/IRect";
import { ISize } from "../../../../interfaces/ISize";
import { ImageRepository } from "../../../../logic/imageRepository/ImageRepository";
import { AppState } from "../../../../store";
import { updateImageDataById } from "../../../../store/labels/actionCreators";
import { ImageData } from "../../../../store/labels/types";
import { FileUtil } from "../../../../utils/FileUtil";
import { RectUtil } from "../../../../utils/RectUtil";
import './ImagePreview.scss';
import { CSSHelper } from "../../../../logic/helpers/CSSHelper";

interface IProps {
    imageData: ImageData;
    style: React.CSSProperties;
    size: ISize;
    isScrolling?: boolean;
    isChecked?: boolean;
    onClick?: () => any;
    isSelected?: boolean;
    updateImageDataById: (id: string, newImageData: ImageData) => any;
}

interface IState {
    image: HTMLImageElement | null;
}

class ImagePreview extends React.Component<IProps, IState> {
    private isLoading: boolean = false;
    private isMountedFlag: boolean = false;

    constructor(props) {
        super(props);

        this.state = {
            image: null,
        }
    }

    public componentDidMount(): void {
        this.isMountedFlag = true;
        this.enqueueImageLoad(this.props.imageData, this.props.isScrolling);
    }

    public componentWillUnmount(): void {
        this.isMountedFlag = false;
    }

    public componentDidUpdate(prevProps: Readonly<IProps>): void {
        if (prevProps.imageData.id !== this.props.imageData.id) {
            if (this.props.imageData.loadStatus || !this.props.isScrolling) {
                this.enqueueImageLoad(this.props.imageData, this.props.isScrolling);
            }
            else {
                this.setState({ image: null });
            }
        }

        if (prevProps.isScrolling && !this.props.isScrolling) {
            this.enqueueImageLoad(this.props.imageData, false);
        }
    }

    shouldComponentUpdate(nextProps: Readonly<IProps>, nextState: Readonly<IState>, nextContext: any): boolean {
        return (
            this.props.imageData.id !== nextProps.imageData.id ||
            this.state.image !== nextState.image ||
            this.props.isSelected !== nextProps.isSelected ||
            this.props.isChecked !== nextProps.isChecked ||
            this.props.isScrolling !== nextProps.isScrolling
        )
    }

    private enqueueImageLoad = (imageData: ImageData, isScrolling: boolean) => {
        ImageLoadManager.addAndRun(() => this.loadImage(imageData, isScrolling));
    };

    private loadImage = async (imageData: ImageData, isScrolling: boolean) => {
        const cachedImage = ImageRepository.getById(imageData.id);
        if (cachedImage) {
            if (this.state.image !== cachedImage && this.isMountedFlag && imageData.id === this.props.imageData.id) {
                this.setState({ image: cachedImage });
            }
            return;
        }

        if (imageData.loadStatus || isScrolling) {
            if (!imageData.loadStatus && imageData.id === this.props.imageData.id && this.state.image !== null) {
                this.setState({ image: null });
            }
            return;
        }

        this.isLoading = true;
        try {
            const image = await FileUtil.loadImage(imageData.fileData);
            this.saveLoadedImage(image, imageData);
        } catch (error) {
            this.handleLoadImageError();
        } finally {
            this.isLoading = false;
        }
    };

    private saveLoadedImage = (image: HTMLImageElement, imageData: ImageData) => {
        const nextImageData: ImageData = {
            ...imageData,
            loadStatus: true
        };

        this.props.updateImageDataById(imageData.id, nextImageData);
        ImageRepository.storeImage(imageData.id, image);
        if (imageData.id === this.props.imageData.id && this.isMountedFlag) {
            this.setState({ image });
        }
    };

    private getStyle = () => {
        const { size } = this.props;

        if (!this.state.image) {
            return {};
        }

        const containerRect: IRect = {
            x: 0.15 * size.width,
            y: 0.15 * size.height,
            width: 0.7 * size.width,
            height: 0.7 * size.height
        };

        const imageRect: IRect = {
            x: 0,
            y: 0,
            width: this.state.image.width,
            height: this.state.image.height
        };

        const imageRatio = RectUtil.getRatio(imageRect);
        const imagePosition: IRect = RectUtil.fitInsideRectWithRatio(containerRect, imageRatio);

        return {
            width: imagePosition.width,
            height: imagePosition.height,
            left: imagePosition.x,
            top: imagePosition.y
        }
    };

    private handleLoadImageError = () => { };

    private getClassName = () => {
        return classNames(
            "ImagePreview",
            {
                "selected": this.props.isSelected,
            }
        );
    };

    public render() {
        const {
            isChecked,
            style,
            onClick
        } = this.props;

        return (
            <div
                className={this.getClassName()}
                style={style}
                onClick={onClick ? onClick : undefined}
            >
                {(!!this.state.image) ?
                    [
                        <div
                            className="Foreground"
                            key={"Foreground"}
                            style={this.getStyle()}
                        >
                            <img
                                className="Image"
                                draggable={false}
                                src={this.state.image.src}
                                alt={this.state.image.alt}
                                style={{ ...this.getStyle(), left: 0, top: 0 }}
                            />
                            {isChecked && <img
                                className="CheckBox"
                                draggable={false}
                                src={"ico/ok.png"}
                                alt={"checkbox"}
                            />}
                        </div>,
                        <div
                            className="Background"
                            key={"Background"}
                            style={this.getStyle()}
                        />
                    ] :
                    <ClipLoader
                        size={30}
                        color={CSSHelper.getLeadingColor()}
                        loading={true}
                    />}
            </div>)
    }
}

const mapDispatchToProps = {
    updateImageDataById
};

const mapStateToProps = (state: AppState) => ({});

export default connect(
    mapStateToProps,
    mapDispatchToProps
)(ImagePreview);
